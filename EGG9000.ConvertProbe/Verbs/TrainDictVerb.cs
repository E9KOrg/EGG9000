using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Google.Protobuf;
using MessagePack;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZstdSharp;

namespace EGG9000.ConvertProbe.Verbs {
    public static class TrainDictVerb {
        public const int DefaultAccountsTrain = 3000;
        public const int DefaultCoopsTrain = 8000;
        public const int Holdout = 500;
        public const int DictionaryCapacity = 112640;
        private const int Repeats = 5;

        private static readonly MessagePackSerializerOptions PlainOptions = StorageMessagePack.Options.WithCompression(MessagePackCompression.None);

        private sealed record Corpus(string Name, string File, List<byte[]> Train, List<byte[]> Eval, StorageCompressionStrategy Current);

        public static async Task<int> RunAsync(ProbeOptions options) {
            var outDir = options.EnsureOutDir();
            var accountsTrain = options.Limit ?? DefaultAccountsTrain;
            var coopsTrain = options.Limit.HasValue ? options.Limit.Value * DefaultCoopsTrain / DefaultAccountsTrain : DefaultCoopsTrain;

            List<byte[]> accountBlobs, coopBlobs;
            await using(var connection = await options.OpenConnectionAsync()) {
                accountBlobs = await ReadBlobsAsync(connection, Sql.RandomAccountBlobs, accountsTrain + Holdout);
                coopBlobs = await ReadBlobsAsync(connection, Sql.RandomCoopBlobs, coopsTrain + Holdout);
            }

            var accountPlains = accountBlobs.Select(AccountPlain).Where(p => p is not null).ToList();
            var coopPlains = coopBlobs.Select(CoopPlain).Where(p => p is not null).ToList();
            var corpora = new[] {
                new Corpus("accounts", "accounts.zdict", accountPlains.Skip(Holdout).ToList(), accountPlains.Take(Holdout).ToList(), StorageCompressionStrategy.AccountGraph),
                new Corpus("coop status", "coopstatus.zdict", coopPlains.Skip(Holdout).ToList(), coopPlains.Take(Holdout).ToList(), StorageCompressionStrategy.CoopStatus)
            };

            var report = new StringBuilder();
            report.Append(Markdown.Heading(1, "traindict"));
            report.AppendLine($"Dictionary capacity {DictionaryCapacity} bytes. Hold-out rows are excluded from training. Timings are min of {Repeats} repeats per blob.");
            report.AppendLine();
            foreach(var corpus in corpora) {
                var timer = Stopwatch.StartNew();
                var dictionary = DictBuilder.TrainFromBuffer(corpus.Train, DictionaryCapacity);
                timer.Stop();
                var path = Path.Combine(outDir, corpus.File);
                await File.WriteAllBytesAsync(path, dictionary);
                report.Append(Markdown.Heading(2, corpus.Name));
                report.AppendLine($"- training samples: {corpus.Train.Count} ({Markdown.Bytes(corpus.Train.Sum(p => (long)p.Length))} plain), hold-out: {corpus.Eval.Count}");
                report.AppendLine($"- dictionary: {path}, {dictionary.Length} bytes, zstd dictionary id {BitConverter.ToUInt32(dictionary, 4)}, trained in {timer.Elapsed.TotalSeconds:F1} s");
                report.AppendLine();
                report.AppendLine(Evaluate(corpus, dictionary));
            }
            var reportPath = Path.Combine(outDir, "traindict.md");
            await File.WriteAllTextAsync(reportPath, report.ToString(), new UTF8Encoding(false));
            Console.Write(report);
            Console.WriteLine($"Report: {reportPath}");
            return 0;
        }

        private static string Evaluate(Corpus corpus, byte[] dictionary) {
            var level = corpus.Current.ZstdLevel;
            var candidate = new Compressor(level);
            candidate.LoadDictionary(dictionary);
            var candidateDecoder = new Decompressor();
            candidateDecoder.LoadDictionary(dictionary);
            var plainNoDict = new Compressor(level);
            var plainNoDictDecoder = new Decompressor();
            var brotli = new StorageCompressionStrategy(StorageCompressionAlgorithm.Brotli);
            var rows = new List<IReadOnlyList<string>>();
            long plainTotal = corpus.Eval.Sum(p => (long)p.Length);
            rows.Add(["plain", Markdown.Bytes(plainTotal), Markdown.Bytes(corpus.Eval.Count == 0 ? 0 : plainTotal / corpus.Eval.Count), "", "", "", "", "", ""]);
            var brotliRow = Measure(corpus.Eval, p => StorageCompression.Compress(p, brotli), StorageCompression.Decompress);
            rows.Add(Format("brotli q6 envelope (T1 stored format)", brotliRow, plainTotal, brotliRow.Total));
            try {
                var current = Measure(corpus.Eval, p => StorageCompression.Compress(p, corpus.Current), StorageCompression.Decompress);
                rows.Add(Format($"current strategy ({corpus.Current.Algorithm} L{level}, embedded dictionary {corpus.Current.DictionaryId})", current, plainTotal, brotliRow.Total));
            } catch(InvalidOperationException e) {
                rows.Add([$"current strategy ({corpus.Current.Algorithm} L{level}, embedded dictionary {corpus.Current.DictionaryId})", "unavailable: " + e.Message, "", "", "", "", "", "", ""]);
            }
            var noDict = Measure(corpus.Eval, p => plainNoDict.Wrap(p).ToArray(), c => plainNoDictDecoder.Unwrap(c).ToArray());
            rows.Add(Format($"zstd L{level} no dictionary", noDict, plainTotal, brotliRow.Total));
            var trained = Measure(corpus.Eval, p => candidate.Wrap(p).ToArray(), c => candidateDecoder.Unwrap(c).ToArray());
            rows.Add(Format($"zstd L{level} with this dictionary", trained, plainTotal, brotliRow.Total));
            return Markdown.Table(["format", "total bytes", "avg per blob", "vs plain", "vs brotli q6", "compress median us", "compress p95 us", "decompress median us", "decompress p95 us"], rows);
        }

        private sealed record Measured(long Total, List<double> Compress, List<double> Decompress);

        private static Measured Measure(List<byte[]> plains, Func<byte[], byte[]> compress, Func<byte[], byte[]> decompress) {
            for(var i = 0; i < 20 && plains.Count > 0; i++)
                decompress(compress(plains[i % plains.Count]));
            long total = 0;
            var compressUs = new List<double>();
            var decompressUs = new List<double>();
            foreach(var plain in plains) {
                byte[] packed = null;
                compressUs.Add(Min(() => packed = compress(plain)));
                byte[] back = null;
                decompressUs.Add(Min(() => back = decompress(packed)));
                if(!back.AsSpan().SequenceEqual(plain))
                    throw new InvalidDataException("Round trip mismatch during dictionary evaluation.");
                total += packed.Length;
            }
            return new Measured(total, compressUs, decompressUs);
        }

        private static IReadOnlyList<string> Format(string name, Measured measured, long plainTotal, long brotliTotal) {
            var count = measured.Compress.Count;
            return [
                name, Markdown.Bytes(measured.Total), Markdown.Bytes(count == 0 ? 0 : measured.Total / count),
                Delta(measured.Total, plainTotal), Delta(measured.Total, brotliTotal),
                Markdown.Num(Percentile(measured.Compress, 0.5), 1), Markdown.Num(Percentile(measured.Compress, 0.95), 1),
                Markdown.Num(Percentile(measured.Decompress, 0.5), 1), Markdown.Num(Percentile(measured.Decompress, 0.95), 1)
            ];
        }

        private static string Delta(long candidate, long baseline) => baseline == 0 ? "n/a" : (100.0 * (candidate - baseline) / baseline).ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";

        private static byte[] AccountPlain(byte[] stored) {
            try {
                if(StorageCompression.IsEnveloped(stored))
                    return StorageCompression.Decompress(stored);
                return MessagePackSerializer.Serialize(StorageCodec.Unpack<List<EggIncAccount>>(stored), PlainOptions);
            } catch(Exception) {
                return null;
            }
        }

        private static byte[] CoopPlain(byte[] stored) {
            try {
                if(StorageCompression.IsEnveloped(stored))
                    return StorageCompression.Decompress(stored);
                return CoopStatusCodec.Decode(stored).ToByteArray();
            } catch(Exception) {
                return null;
            }
        }

        private static async Task<List<byte[]>> ReadBlobsAsync(NpgsqlConnection connection, string sql, int limit) {
            var blobs = new List<byte[]>();
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 600 };
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync();
            while(await reader.ReadAsync())
                blobs.Add(reader.GetFieldValue<byte[]>(0));
            return blobs;
        }

        private static double Min(Action action) {
            var best = double.MaxValue;
            for(var i = 0; i < Repeats; i++) {
                var start = Stopwatch.GetTimestamp();
                action();
                var micros = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                if(micros < best) best = micros;
            }
            return best;
        }

        private static double Percentile(List<double> samples, double p) {
            if(samples.Count == 0) return 0;
            var sorted = samples.OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
        }
    }
}
