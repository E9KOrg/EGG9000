using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using MessagePack;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public sealed class BenchVerb {
        private const int DefaultLimit = 200;
        private const int Repeats = 5;
        private const int WarmupIterations = 20;
        private const int DistinctLengthsShown = 12;

        private static readonly MessagePackSerializerOptions MasterOptions = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        private sealed class OpTimings(string name) {
            public string Name { get; } = name;
            public List<double> Micros { get; } = [];
        }

        private sealed class Lz4Layout(string name) {
            public string Name { get; } = name;
            public int Blobs;
            public int Framed;
            public List<double> BlocksPerBlob { get; } = [];
            public Dictionary<int, int> BlockLengths { get; } = new();

            public void Add(byte[] blob) {
                Blobs++;
                var (blocks, lengths) = Lz4BlockLengths(blob);
                BlocksPerBlob.Add(blocks);
                if(blocks == 0) return;
                Framed++;
                foreach(var length in lengths)
                    BlockLengths[length] = BlockLengths.GetValueOrDefault(length) + 1;
            }
        }

        private sealed class SizeTotals {
            public long Stored;
            public long Legacy;
            public long Envelope;
            public long? Master;
            public int Blobs;
            public int Failed;
            public SortedDictionary<string, int> StoredFormats { get; } = new(StringComparer.Ordinal);
            public List<Lz4Layout> Layouts { get; } = [];

            public Lz4Layout Layout(string name) {
                var layout = Layouts.FirstOrDefault(l => l.Name == name);
                if(layout is null) Layouts.Add(layout = new Lz4Layout(name));
                return layout;
            }
        }

        public static async Task<int> RunAsync(ProbeOptions options) {
            var limit = options.Limit ?? DefaultLimit;
            var outDir = options.EnsureOutDir();
            List<byte[]> accountBlobs, coopBlobs;
            await using(var connection = await options.OpenConnectionAsync()) {
                accountBlobs = await ReadBlobsAsync(connection, Sql.RandomAccountBlobs, limit);
                coopBlobs = await ReadBlobsAsync(connection, Sql.PreferActiveCoopBlobs, limit);
            }

            var originalCompress = StorageCodec.CompressWriteEnabled;
            var originalProto = CoopStatusCodec.ProtoWriteEnabled;
            var report = new StringBuilder();
            try {
                report.Append(Markdown.Heading(2, $"Accounts blob ({Sql.UsersTable}.{Sql.UsersAccountsBlob}), {accountBlobs.Count} random rows"));
                report.Append(Bench(accountBlobs, AccountOps));
                report.Append(Markdown.Heading(2, $"Coop status blob ({Sql.CoopsTable}.{Sql.CoopsStatusBlob}), {coopBlobs.Count} rows (polled coops preferred)"));
                report.Append(Bench(coopBlobs, CoopOps));
            } finally {
                StorageCodec.CompressWriteEnabled = originalCompress;
                CoopStatusCodec.ProtoWriteEnabled = originalProto;
            }
            report.AppendLine($"Repeats per op per blob: {Repeats} (min taken). Warm-up iterations: {WarmupIterations}.");
            Console.Write(report);
            var reportPath = Path.Combine(outDir, "bench.md");
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"Report: {reportPath}");
            return 0;
        }

        private static async Task<List<byte[]>> ReadBlobsAsync(NpgsqlConnection connection, string sql, int limit) {
            var blobs = new List<byte[]>();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("limit", limit);
            await using var reader = await command.ExecuteReaderAsync();
            while(await reader.ReadAsync())
                blobs.Add(reader.GetFieldValue<byte[]>(0));
            return blobs;
        }

        private static string Bench(List<byte[]> blobs, Action<byte[], Dictionary<string, OpTimings>, SizeTotals> ops) {
            if(blobs.Count == 0) return "No rows." + Environment.NewLine + Environment.NewLine;
            var warmTimings = new Dictionary<string, OpTimings>(StringComparer.Ordinal);
            var warmSizes = new SizeTotals();
            for(var i = 0; i < WarmupIterations; i++)
                RunOps(blobs[i % blobs.Count], warmTimings, warmSizes, ops);

            var timings = new Dictionary<string, OpTimings>(StringComparer.Ordinal);
            var sizes = new SizeTotals();
            foreach(var blob in blobs)
                RunOps(blob, timings, sizes, ops);

            var output = new StringBuilder();
            output.AppendLine(Markdown.Table(["op", "blobs", "median us", "p95 us", "total ms"],
                timings.Values.Select(t => (IReadOnlyList<string>)[
                    t.Name, Markdown.Num(t.Micros.Count), Markdown.Num(Percentile(t.Micros, 0.5), 1),
                    Markdown.Num(Percentile(t.Micros, 0.95), 1), Markdown.Num(t.Micros.Sum() / 1000.0, 2)
                ])));
            List<IReadOnlyList<string>> sizeRows = [
                ["stored", Markdown.Bytes(sizes.Stored), Avg(sizes.Stored, sizes.Blobs), "", ""],
                ["legacy re-encode", Markdown.Bytes(sizes.Legacy), Avg(sizes.Legacy, sizes.Blobs), Reduction(sizes.Legacy, sizes.Stored), ""],
                ["envelope re-encode", Markdown.Bytes(sizes.Envelope), Avg(sizes.Envelope, sizes.Blobs), Reduction(sizes.Envelope, sizes.Stored), Reduction(sizes.Envelope, sizes.Legacy)]
            ];
            if(sizes.Master is { } master)
                sizeRows.Add(["master-equivalent re-encode", Markdown.Bytes(master), Avg(master, sizes.Blobs), Reduction(master, sizes.Stored), Reduction(master, sizes.Legacy)]);
            output.AppendLine(Markdown.Table(["size", "total", "avg per blob", "vs stored", "vs legacy"], sizeRows));
            output.AppendLine("Stored formats: " + string.Join(", ", sizes.StoredFormats.Select(kv => $"{kv.Key} {kv.Value}")) + $". Decode failures skipped: {sizes.Failed}.");
            output.AppendLine();
            if(sizes.Layouts.Count > 0) {
                output.Append(Markdown.Heading(3, "LZ4 block layout (MessagePack Lz4BlockArray framing)"));
                output.AppendLine(Markdown.Table(["bytes", "blobs", "framed", "blocks median", "blocks max", "distinct block lengths", "block lengths (uncompressed, x occurrences)"],
                    sizes.Layouts.Select(l => (IReadOnlyList<string>)[
                        l.Name, Markdown.Num(l.Blobs), Markdown.Num(l.Framed), Markdown.Num(Percentile(l.BlocksPerBlob, 0.5), 0),
                        Markdown.Num(l.BlocksPerBlob.Count == 0 ? 0 : l.BlocksPerBlob.Max(), 0), Markdown.Num(l.BlockLengths.Count), LengthsSeen(l.BlockLengths)
                    ])));
                output.AppendLine("Framed = top-level array whose first element is ext type Lz4BlockArray; blocks = array element count - 1; unframed blobs (plain msgpack, under the 64 byte compression threshold) count as 0 blocks. Stored bytes are only parsed when the stored format is legacy msgpack.");
                output.AppendLine();
            }
            return output.ToString();
        }

        private static string LengthsSeen(Dictionary<int, int> lengths) {
            var ordered = lengths.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToList();
            var shown = string.Join(", ", ordered.Take(DistinctLengthsShown).Select(kv => $"{kv.Key} x{kv.Value}"));
            return ordered.Count > DistinctLengthsShown ? shown + $", +{ordered.Count - DistinctLengthsShown} more" : shown;
        }

        private static (int Blocks, int[] Lengths) Lz4BlockLengths(byte[] blob) {
            try {
                var reader = new MessagePackReader(blob);
                if(reader.NextMessagePackType != MessagePackType.Array) return (0, []);
                var count = reader.ReadArrayHeader();
                if(count < 2 || reader.NextMessagePackType != MessagePackType.Extension) return (0, []);
                var ext = reader.ReadExtensionFormat();
                if(ext.Header.TypeCode != ReservedExtensionTypeCodes.Lz4BlockArray) return (0, []);
                var lengths = new List<int>(count - 1);
                var inner = new MessagePackReader(ext.Data);
                while(!inner.End) lengths.Add(inner.ReadInt32());
                for(var i = 1; i < count; i++) reader.ReadBytes();
                return (count - 1, [.. lengths]);
            } catch(MessagePackSerializationException) {
                return (0, []);
            }
        }

        private static void RunOps(byte[] blob, Dictionary<string, OpTimings> timings, SizeTotals sizes, Action<byte[], Dictionary<string, OpTimings>, SizeTotals> ops) {
            try {
                ops(blob, timings, sizes);
            } catch(Exception) {
                sizes.Failed++;
            }
        }

        private static void AccountOps(byte[] stored, Dictionary<string, OpTimings> timings, SizeTotals sizes) {
            List<EggIncAccount> decoded = null;
            byte[] legacy = null, envelope = null, master = null;
            Record(timings, "decode stored", () => decoded = StorageCodec.Unpack<List<EggIncAccount>>(stored));
            Record(timings, "encode legacy", () => {
                StorageCodec.CompressWriteEnabled = false;
                legacy = StorageCodec.Pack(decoded);
            });
            Record(timings, "encode envelope", () => {
                StorageCodec.CompressWriteEnabled = true;
                envelope = StorageCodec.Pack(decoded);
            });
            Record(timings, "encode master-equivalent", () => master = MessagePackSerializer.Serialize(decoded, MasterOptions));
            Record(timings, "decode legacy", () => StorageCodec.Unpack<List<EggIncAccount>>(legacy));
            Record(timings, "decode envelope", () => StorageCodec.Unpack<List<EggIncAccount>>(envelope));
            Tally(sizes, stored, legacy, envelope, master);
            if(AccountDecoder.FormatOf(stored) == "legacy") sizes.Layout("stored").Add(stored);
            sizes.Layout("legacy re-encode").Add(legacy);
            sizes.Layout("master-equivalent re-encode").Add(master);
        }

        private static void CoopOps(byte[] stored, Dictionary<string, OpTimings> timings, SizeTotals sizes) {
            Ei.ContractCoopStatusResponse decoded = null;
            byte[] legacy = null, envelope = null;
            Record(timings, "decode stored", () => decoded = CoopStatusCodec.Decode(stored));
            Record(timings, "encode legacy", () => {
                CoopStatusCodec.ProtoWriteEnabled = false;
                legacy = CoopStatusCodec.Encode(decoded);
            });
            Record(timings, "encode envelope", () => {
                CoopStatusCodec.ProtoWriteEnabled = true;
                envelope = CoopStatusCodec.Encode(decoded);
            });
            Record(timings, "decode legacy", () => CoopStatusCodec.Decode(legacy));
            Record(timings, "decode envelope", () => CoopStatusCodec.Decode(envelope));
            Tally(sizes, stored, legacy, envelope);
        }

        private static void Tally(SizeTotals sizes, byte[] stored, byte[] legacy, byte[] envelope, byte[] master = null) {
            sizes.Blobs++;
            sizes.Stored += stored.Length;
            sizes.Legacy += legacy.Length;
            sizes.Envelope += envelope.Length;
            if(master is not null) sizes.Master = (sizes.Master ?? 0) + master.Length;
            var format = AccountDecoder.FormatOf(stored);
            var algo = AccountDecoder.AlgoOf(stored);
            var key = algo.Length == 0 ? format : format + "/" + algo;
            sizes.StoredFormats[key] = sizes.StoredFormats.GetValueOrDefault(key) + 1;
        }

        private static void Record(Dictionary<string, OpTimings> timings, string name, Action action) {
            var best = double.MaxValue;
            for(var i = 0; i < Repeats; i++) {
                var start = Stopwatch.GetTimestamp();
                action();
                var micros = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                if(micros < best) best = micros;
            }
            if(!timings.TryGetValue(name, out var op)) timings[name] = op = new OpTimings(name);
            op.Micros.Add(best);
        }

        private static double Percentile(List<double> samples, double p) {
            if(samples.Count == 0) return 0;
            var sorted = samples.OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling(p * sorted.Count) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
        }

        private static string Avg(long total, int count) => count == 0 ? "0" : Markdown.Bytes(total / count);

        private static string Reduction(long candidate, long baseline) => baseline == 0 ? "n/a" : Markdown.Percent(baseline - candidate, baseline) + " smaller";
    }
}
