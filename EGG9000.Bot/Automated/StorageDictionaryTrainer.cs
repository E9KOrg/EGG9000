using EGG9000.Common.Consumers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Google.Protobuf;

using MassTransit;

using MessagePack;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ZstdSharp;

namespace EGG9000.Bot.Automated {
    public sealed class StorageDictionaryTrainer(IServiceScopeFactory scopeFactory, IPublishEndpoint publish, ILogger<StorageDictionaryTrainer> logger)
        : PeriodicBackgroundService(TimeSpan.FromHours(24), TimeSpan.FromMinutes(15), logger) {
        public const string AutomationLogType = "StorageDictionaryTrainer";
        public const int AccountsTrain = 3000;
        public const int CoopsTrain = 8000;
        public const int Holdout = 500;
        public const int DictionaryCapacity = 112640;
        public const double AdoptRatio = 0.95;
        public const int SampleCommandTimeoutSeconds = 600;
        public static readonly TimeSpan RetrainAge = TimeSpan.FromDays(30);

        private static readonly MessagePackSerializerOptions PlainOptions = StorageMessagePack.Options.WithCompression(MessagePackCompression.None);

        private sealed record CorpusSpec(string Name, bool WriteEnabled, string Fingerprint, StorageCompressionStrategy Strategy, int Train, Func<ApplicationDbContext, int, CancellationToken, Task<List<byte[]>>> Read, Func<byte[], byte[]> Plain);

        public static bool ShouldTrain(string activeFingerprint, string currentFingerprint, DateTimeOffset? evaluatedAt, DateTimeOffset now) {
            if(!string.Equals(activeFingerprint, currentFingerprint, StringComparison.Ordinal))
                return true;
            return evaluatedAt is null || now - evaluatedAt.Value >= RetrainAge;
        }

        public static bool ShouldAdopt(long activeBytes, long candidateBytes) {
            return activeBytes > 0 && candidateBytes <= (long)Math.Floor(activeBytes * AdoptRatio);
        }

        public static int NextId(IEnumerable<int> usedIds) => usedIds.DefaultIfEmpty(0).Max() + 1;

        protected override async Task DoWorkAsync(CancellationToken token) {
            var started = DateTimeOffset.UtcNow;
            var specs = new[] {
                new CorpusSpec(StorageDictionaryRow.AccountsCorpus, StorageCodec.CompressWriteEnabled, StorageFingerprint.Accounts(), StorageCompressionStrategy.AccountGraph, AccountsTrain, ReadAccountBlobsAsync, AccountPlain),
                new CorpusSpec(StorageDictionaryRow.CoopStatusCorpus, CoopStatusCodec.ProtoWriteEnabled, StorageFingerprint.CoopStatus(), StorageCompressionStrategy.CoopStatus, CoopsTrain, ReadCoopBlobsAsync, CoopPlain)
            };
            foreach(var spec in specs) {
                try {
                    await ProcessAsync(spec, token);
                } catch(OperationCanceledException) when(token.IsCancellationRequested) {
                    throw;
                } catch(Exception e) {
                    _logger.LogError(e, "storage dictionary trainer: {Corpus} failed", spec.Name);
                }
            }
            await WriteAutomationLogAsync(started);
        }

        private async Task ProcessAsync(CorpusSpec spec, CancellationToken token) {
            if(!spec.WriteEnabled) {
                _logger.LogInformation("storage dictionary trainer: {Corpus} writer disabled, skipping", spec.Name);
                return;
            }
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.SetCommandTimeout(SampleCommandTimeoutSeconds);
            var active = await db.StorageDictionaries.AsNoTracking().Where(r => r.Corpus == spec.Name && r.Active).OrderByDescending(r => r.Id).FirstOrDefaultAsync(token);
            var now = DateTimeOffset.UtcNow;
            if(!ShouldTrain(active?.Fingerprint, spec.Fingerprint, active?.EvaluatedAt, now)) {
                _logger.LogInformation("storage dictionary trainer: {Corpus} dictionary {Id} current (fingerprint match, evaluated {EvaluatedAt})", spec.Name, active.Id, active.EvaluatedAt);
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var blobs = await spec.Read(db, spec.Train + Holdout, token);
            var plains = blobs.Select(spec.Plain).Where(p => p is not null).ToList();
            if(plains.Count < Holdout * 2) {
                _logger.LogWarning("storage dictionary trainer: {Corpus} only {Count} decodable samples, need at least {Min}", spec.Name, plains.Count, Holdout * 2);
                await RecordEvaluationAsync(db, active, spec, now, 0, 0, token);
                return;
            }
            var holdout = plains.Take(Holdout).ToList();
            var train = plains.Skip(Holdout).ToList();
            var candidate = DictBuilder.TrainFromBuffer(train, DictionaryCapacity);
            var level = spec.Strategy.ZstdLevel;
            var plainBytes = holdout.Sum(p => (long)p.Length);
            var activeBytes = Total(holdout, spec.Strategy.Dictionary?.Bytes, level);
            var candidateBytes = Total(holdout, candidate, level);
            var adopt = ShouldAdopt(activeBytes, candidateBytes);
            _logger.LogInformation("storage dictionary trainer: {Corpus} trained on {Train} samples in {Elapsed}; hold-out {Holdout} rows plain {Plain} B, active dict {ActiveId} {Active} B, candidate {Candidate} B ({Ratio:P2}), adopt={Adopt}",
                spec.Name, train.Count, stopwatch.Elapsed, holdout.Count, plainBytes, spec.Strategy.DictionaryId, activeBytes, candidateBytes, (double)candidateBytes / activeBytes, adopt);
            if(!adopt) {
                await RecordEvaluationAsync(db, active, spec, now, plainBytes, activeBytes, token);
                return;
            }

            var usedIds = (await db.StorageDictionaries.AsNoTracking().Select(r => r.Id).ToListAsync(token)).Concat(StorageDictionary.Registry.Select(d => (int)d.Id));
            var id = NextId(usedIds);
            if(id > byte.MaxValue) {
                _logger.LogError("storage dictionary trainer: {Corpus} next id {Id} exceeds the envelope dictionary byte, not adopting", spec.Name, id);
                return;
            }
            var row = new StorageDictionaryRow {
                Id = id,
                Corpus = spec.Name,
                Fingerprint = spec.Fingerprint,
                TrainedAt = now,
                EvaluatedAt = now,
                SampleCount = train.Count,
                HoldoutBytesPlain = plainBytes,
                HoldoutBytesActive = activeBytes,
                HoldoutBytesCandidate = candidateBytes,
                Bytes = candidate,
                Active = true
            };
            await using(var transaction = await db.Database.BeginTransactionAsync(token)) {
                await db.StorageDictionaries.Where(r => r.Corpus == spec.Name && r.Active).ExecuteUpdateAsync(s => s.SetProperty(r => r.Active, false), token);
                db.StorageDictionaries.Add(row);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            }
            StorageDictionaryLoader.Apply(row);
            await publish.Publish(new StorageDictionaryAdoptedMessage { Id = id, Corpus = spec.Name }, token);
            _logger.LogInformation("storage dictionary trainer: {Corpus} adopted dictionary {Id}; run the storage sweep CLI to converge existing rows", spec.Name, id);
        }

        private static async Task RecordEvaluationAsync(ApplicationDbContext db, StorageDictionaryRow active, CorpusSpec spec, DateTimeOffset now, long plainBytes, long activeBytes, CancellationToken token) {
            if(active is not null) {
                await db.StorageDictionaries.Where(r => r.Id == active.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Fingerprint, spec.Fingerprint).SetProperty(r => r.EvaluatedAt, now), token);
                return;
            }
            var embedded = spec.Strategy.Dictionary;
            if(embedded is null)
                return;
            db.StorageDictionaries.Add(new StorageDictionaryRow {
                Id = embedded.Id,
                Corpus = spec.Name,
                Fingerprint = spec.Fingerprint,
                TrainedAt = now,
                EvaluatedAt = now,
                SampleCount = 0,
                HoldoutBytesPlain = plainBytes,
                HoldoutBytesActive = activeBytes,
                HoldoutBytesCandidate = activeBytes,
                Bytes = embedded.Bytes,
                Active = true
            });
            await db.SaveChangesAsync(token);
        }

        private static long Total(List<byte[]> plains, byte[] dictionary, int level) {
            using var compressor = new Compressor(level);
            if(dictionary is not null)
                compressor.LoadDictionary(dictionary);
            long total = 0;
            foreach(var plain in plains)
                total += compressor.Wrap(plain).Length;
            return total;
        }

        private static Task<List<byte[]>> ReadAccountBlobsAsync(ApplicationDbContext db, int limit, CancellationToken token) {
            return db.Database.SqlQuery<byte[]>($"""
                SELECT "_contractRegistrationByte" AS "Value"
                FROM "Users"
                WHERE "_contractRegistrationByte" IS NOT NULL AND octet_length("_contractRegistrationByte") > 0
                ORDER BY random()
                LIMIT {limit}
                """).ToListAsync(token);
        }

        private static Task<List<byte[]>> ReadCoopBlobsAsync(ApplicationDbContext db, int limit, CancellationToken token) {
            return db.Database.SqlQuery<byte[]>($"""
                SELECT "_StatusCompressed" AS "Value"
                FROM "Coops"
                WHERE "_StatusCompressed" IS NOT NULL AND octet_length("_StatusCompressed") > 0
                  AND "CreatorID" IS DISTINCT FROM {Coop.TestSeedCreatorId}
                ORDER BY random()
                LIMIT {limit}
                """).ToListAsync(token);
        }

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

        private async Task WriteAutomationLogAsync(DateTimeOffset started) {
            try {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.AutomationLogs.Add(new AutomationLog { Type = AutomationLogType, StartTime = started, EndTime = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(CancellationToken.None);
            } catch(Exception e) {
                _logger.LogError(e, "storage dictionary trainer could not write its AutomationLog row");
            }
        }
    }
}
