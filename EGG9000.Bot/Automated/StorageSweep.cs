using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public record StorageSweepOptions(bool Enabled, int BatchDelayMs) {
        public const string EnabledVariable = "EGG9000_STORAGE_SWEEP";
        public const string BatchDelayVariable = "EGG9000_STORAGE_SWEEP_BATCH_DELAY_MS";
        public const int DefaultBatchDelayMs = 0;

        public static StorageSweepOptions FromEnvironment() {
            return Parse(Environment.GetEnvironmentVariable(EnabledVariable), Environment.GetEnvironmentVariable(BatchDelayVariable));
        }

        public static StorageSweepOptions Parse(string enabledRaw, string delayRaw) {
            var enabled = string.Equals(enabledRaw, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase);
            var delay = int.TryParse(delayRaw, out var parsed) ? Math.Max(0, parsed) : DefaultBatchDelayMs;
            return new StorageSweepOptions(enabled, delay);
        }
    }

    public sealed class StorageSweepCounters(string table) {
        public string Table { get; } = table;
        public int Scanned { get; set; }
        public int Converted { get; set; }
        public int Current { get; set; }
        public int SkippedChanged { get; set; }
        public int Failed { get; set; }
        public long BytesBefore { get; set; }
        public long BytesAfter { get; set; }

        public override string ToString() {
            return $"{Table}: scanned {Scanned}, converted {Converted}, current {Current}, skippedChanged {SkippedChanged}, failed {Failed}, bytes {BytesBefore} to {BytesAfter}";
        }
    }

    public sealed class StorageSweep(IServiceScopeFactory scopeFactory, ILogger<StorageSweep> logger) : IHostedService, IDisposable {
        public const string AutomationLogType = "StorageSweep";
        public const int UsersBatchSize = 500;
        public const int CoopsBatchSize = 4000;
        public const int CommandTimeoutSeconds = 1800;

        private const string UsersColumn = "\"_contractRegistrationByte\"";
        private const string CoopsColumn = "\"_StatusCompressed\"";

        public const string UsersCasUpdateSql = "UPDATE \"Users\" SET \"_contractRegistrationByte\" = @new WHERE \"Id\" = @id AND \"_contractRegistrationByte\" = @old";
        public const string CoopsCasUpdateSql = "UPDATE \"Coops\" SET \"_StatusCompressed\" = @new WHERE \"Id\" = @id AND \"_StatusCompressed\" = @old";

        public static string UsersPredicate {
            get { return UsersTarget.Predicate; }
        }
        public static string UsersCountSql {
            get { return UsersTarget.CountSql; }
        }
        public static string UsersBatchSql {
            get { return UsersTarget.BatchSql; }
        }

        public static string CoopsPredicate {
            get { return CoopsTarget.Predicate; }
        }
        public static string CoopsCountSql {
            get { return CoopsTarget.CountSql; }
        }
        public static string CoopsBatchSql {
            get { return CoopsTarget.BatchSql; }
        }

        private static SweepTarget _usersTarget;
        private static SweepTarget _coopsTarget;

        private static SweepTarget UsersTarget {
            get { return CachedTarget(ref _usersTarget, "Users", UsersColumn, UsersCasUpdateSql, UsersBatchSize, StorageCompressionStrategy.AccountGraph, StorageSweepCodec.Accounts); }
        }
        private static SweepTarget CoopsTarget {
            get { return CachedTarget(ref _coopsTarget, "Coops", CoopsColumn, CoopsCasUpdateSql, CoopsBatchSize, StorageCompressionStrategy.CoopStatus, StorageSweepCodec.CoopStatus); }
        }

        private readonly CancellationTokenSource _stopping = new();
        private Task _run = Task.CompletedTask;

        private sealed record SweepTarget(string Table, string Predicate, string CountSql, string BatchSql, string UpdateSql, int BatchSize, StorageCompressionStrategy Strategy, Func<byte[], SweepOutcome> Reencode);

        private static SweepTarget CachedTarget(ref SweepTarget cached, string table, string column, string updateSql, int batchSize, StorageCompressionStrategy strategy, Func<byte[], SweepOutcome> reencode) {
            if(cached is not null && cached.Strategy == strategy)
                return cached;
            var predicate = StalePredicate(column, strategy);
            return cached = new SweepTarget(
                table,
                predicate,
                $"SELECT COUNT(*) FROM \"{table}\" WHERE {predicate}",
                $"SELECT \"Id\", {column} FROM \"{table}\" WHERE {predicate} AND \"Id\" > @lastId ORDER BY \"Id\" LIMIT @batch",
                updateSql,
                batchSize,
                strategy,
                reencode);
        }

        public static string StalePredicate(string column, StorageCompressionStrategy strategy) {
            var firstByte = $"CASE WHEN octet_length({column}) > 0 THEN get_byte({column}, 0) END";
            var algorithm = $"COALESCE(CASE WHEN octet_length({column}) > 1 THEN get_byte({column}, 1) END, -1)";
            var dictionary = $"COALESCE(CASE WHEN octet_length({column}) > 2 THEN get_byte({column}, 2) END, -1)";
            var rawCurrent = $"({algorithm} = @raw AND octet_length({column}) <= @rawMax)";
            var encodedCurrent = strategy.Algorithm == StorageCompressionAlgorithm.Zstd
                ? $"({algorithm} = @algo AND {dictionary} = @dict)"
                : $"({algorithm} = @algo)";
            return $"{column} IS NOT NULL AND octet_length({column}) > 0 AND NOT ({firstByte} = @marker AND ({rawCurrent} OR {encodedCurrent}))";
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            if(!StorageSweepOptions.FromEnvironment().Enabled) {
                logger.LogInformation("storage sweep disabled ({Variable} not set)", StorageSweepOptions.EnabledVariable);
                return Task.CompletedTask;
            }
            logger.LogInformation("storage sweep enabled, running in the background");
            _run = Task.Run(() => RunOnceAsync(_stopping.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            await _stopping.CancelAsync();
            try {
                await _run.WaitAsync(cancellationToken);
            } catch(OperationCanceledException) {
                logger.LogWarning("storage sweep did not finish before shutdown timeout");
            }
        }

        public void Dispose() {
            _stopping.Dispose();
        }

        public Task RunOnceAsync(CancellationToken token) {
            return RunOnceAsync(StorageSweepOptions.FromEnvironment(), token);
        }

        public async Task RunOnceAsync(StorageSweepOptions options, CancellationToken token) {
            if(!options.Enabled)
                return;
            var started = DateTimeOffset.UtcNow;
            var usersTarget = UsersTarget;
            var coopsTarget = CoopsTarget;
            var users = new StorageSweepCounters(usersTarget.Table);
            var coops = new StorageSweepCounters(coopsTarget.Table);
            var stopwatch = Stopwatch.StartNew();
            try {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var connection = db.Database.GetDbConnection();
                logger.LogInformation("storage sweep starting: database {Database} on {DataSource}, compress={Compress}, proto={Proto}, batch delay {Delay} ms",
                    connection.Database, connection.DataSource, StorageCodec.CompressWriteEnabled, CoopStatusCodec.ProtoWriteEnabled, options.BatchDelayMs);
                logger.LogInformation("storage sweep: target format accounts {AccountsAlgo} dict {AccountsDict}, coop status {CoopAlgo} dict {CoopDict}",
                    usersTarget.Strategy.Algorithm, usersTarget.Strategy.DictionaryId, coopsTarget.Strategy.Algorithm, coopsTarget.Strategy.DictionaryId);
                await Task.WhenAll(
                    SweepTableAsync(usersTarget, users, options, token),
                    SweepTableAsync(coopsTarget, coops, options, token));
                logger.LogInformation("storage sweep complete in {Elapsed}. {Users}. {Coops}", stopwatch.Elapsed, users, coops);
            } catch(OperationCanceledException) {
                logger.LogInformation("storage sweep cancelled after {Elapsed}. {Users}. {Coops}", stopwatch.Elapsed, users, coops);
            } catch(Exception e) {
                logger.LogError(e, "storage sweep aborted after {Elapsed}. {Users}. {Coops}", stopwatch.Elapsed, users, coops);
            }
            await WriteAutomationLogAsync(started);
        }

        private async Task SweepTableAsync(SweepTarget target, StorageSweepCounters counters, StorageSweepOptions options, CancellationToken token) {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(token);
            try {
                await SweepTableAsync(connection, target, counters, options, token);
            } finally {
                await db.Database.CloseConnectionAsync();
            }
        }

        private async Task SweepTableAsync(DbConnection connection, SweepTarget target, StorageSweepCounters counters, StorageSweepOptions options, CancellationToken token) {
            var stopwatch = Stopwatch.StartNew();
            var batchNumber = 0;
            var pending = ReadBatchAsync(connection, target, Guid.Empty, token);
            try {
                while(true) {
                    token.ThrowIfCancellationRequested();
                    var rows = await pending;
                    if(rows.Count == 0)
                        break;
                    pending = ReadAheadAsync(target, rows[^1].Id, token);
                    batchNumber++;
                    var encodeWatch = Stopwatch.StartNew();
                    var outcomes = new SweepOutcome[rows.Count];
                    Parallel.For(0, rows.Count, new ParallelOptions { CancellationToken = token }, i => outcomes[i] = target.Reencode(rows[i].Stored));
                    encodeWatch.Stop();

                    var converted = TallyOutcomes(target, counters, rows, outcomes);

                    var writeWatch = Stopwatch.StartNew();
                    var affected = await CasUpdateBatchAsync(connection, target.UpdateSql, converted, token);
                    writeWatch.Stop();
                    TallyWrites(counters, converted, affected);

                    logger.LogInformation("storage sweep: {Table} batch {Batch}, {Counters}, encode {EncodeMs} ms, write {WriteMs} ms, elapsed {Elapsed}",
                        target.Table, batchNumber, counters, encodeWatch.ElapsedMilliseconds, writeWatch.ElapsedMilliseconds, stopwatch.Elapsed);
                    if(options.BatchDelayMs > 0)
                        await Task.Delay(options.BatchDelayMs, token);
                }
            } finally {
                await pending.ContinueWith(t => t.Exception, TaskScheduler.Default);
            }
        }

        private List<(Guid Id, byte[] Old, byte[] Updated)> TallyOutcomes(SweepTarget target, StorageSweepCounters counters, List<(Guid Id, byte[] Stored)> rows, SweepOutcome[] outcomes) {
            var converted = new List<(Guid Id, byte[] Old, byte[] Updated)>(rows.Count);
            for(var i = 0; i < rows.Count; i++) {
                var (id, stored) = rows[i];
                counters.Scanned++;
                switch(outcomes[i].Kind) {
                    case SweepOutcomeKind.Current:
                        counters.Current++;
                        break;
                    case SweepOutcomeKind.Failed:
                        counters.Failed++;
                        logger.LogError(outcomes[i].Error, "storage sweep: {Table} row {Id} could not be re-encoded ({Length} bytes, head {Head})",
                            target.Table, id, stored.Length, Convert.ToHexString(stored.AsSpan(0, Math.Min(12, stored.Length))));
                        break;
                    case SweepOutcomeKind.Converted:
                        converted.Add((id, stored, outcomes[i].Bytes));
                        break;
                }
            }
            return converted;
        }

        private static void TallyWrites(StorageSweepCounters counters, List<(Guid Id, byte[] Old, byte[] Updated)> converted, int[] affected) {
            for(var i = 0; i < converted.Count; i++) {
                if(affected[i] == 0) {
                    counters.SkippedChanged++;
                } else {
                    counters.Converted++;
                    counters.BytesBefore += converted[i].Old.Length;
                    counters.BytesAfter += converted[i].Updated.Length;
                }
            }
        }

        private static void AddFormatParameters(DbCommand command, StorageCompressionStrategy strategy) {
            AddParameter(command, "marker", (int)StorageCompression.Marker);
            AddParameter(command, "raw", (int)StorageCompressionAlgorithm.Raw);
            AddParameter(command, "rawMax", strategy.RawThreshold + StorageCompression.HeaderLength(StorageCompressionAlgorithm.Raw));
            AddParameter(command, "algo", (int)strategy.Algorithm);
            if(strategy.Algorithm == StorageCompressionAlgorithm.Zstd)
                AddParameter(command, "dict", (int)strategy.DictionaryId);
        }

        private async Task<List<(Guid Id, byte[] Stored)>> ReadAheadAsync(SweepTarget target, Guid lastId, CancellationToken token) {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(token);
            try {
                return await ReadBatchAsync(connection, target, lastId, token);
            } finally {
                await db.Database.CloseConnectionAsync();
            }
        }

        private static async Task<List<(Guid Id, byte[] Stored)>> ReadBatchAsync(DbConnection connection, SweepTarget target, Guid lastId, CancellationToken token) {
            var rows = new List<(Guid, byte[])>();
            await using var command = connection.CreateCommand();
            command.CommandText = target.BatchSql;
            AddFormatParameters(command, target.Strategy);
            AddParameter(command, "lastId", lastId);
            AddParameter(command, "batch", target.BatchSize);
            await using var reader = await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))
                rows.Add((reader.GetGuid(0), reader.GetFieldValue<byte[]>(1)));
            return rows;
        }

        private static async Task<int[]> CasUpdateBatchAsync(DbConnection connection, string sql, List<(Guid Id, byte[] Old, byte[] Updated)> converted, CancellationToken token) {
            var affected = new int[converted.Count];
            if(converted.Count == 0)
                return affected;
            if(!connection.CanCreateBatch) {
                for(var i = 0; i < converted.Count; i++)
                    affected[i] = await CasUpdateAsync(connection, sql, converted[i], token);
                return affected;
            }
            await using var batch = connection.CreateBatch();
            batch.Timeout = CommandTimeoutSeconds;
            foreach(var row in converted) {
                var command = batch.CreateBatchCommand();
                command.CommandText = sql;
                AddParameter(command.CreateParameter, command.Parameters, "new", row.Updated);
                AddParameter(command.CreateParameter, command.Parameters, "id", row.Id);
                AddParameter(command.CreateParameter, command.Parameters, "old", row.Old);
                batch.BatchCommands.Add(command);
            }
            await batch.ExecuteNonQueryAsync(token);
            for(var i = 0; i < converted.Count; i++)
                affected[i] = batch.BatchCommands[i].RecordsAffected;
            return affected;
        }

        private static async Task<int> CasUpdateAsync(DbConnection connection, string sql, (Guid Id, byte[] Old, byte[] Updated) row, CancellationToken token) {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            AddParameter(command, "new", row.Updated);
            AddParameter(command, "id", row.Id);
            AddParameter(command, "old", row.Old);
            return await command.ExecuteNonQueryAsync(token);
        }

        private static void AddParameter(DbCommand command, string name, object value) {
            command.CommandTimeout = CommandTimeoutSeconds;
            AddParameter(command.CreateParameter, command.Parameters, name, value);
        }

        private static void AddParameter(Func<DbParameter> create, DbParameterCollection parameters, string name, object value) {
            var parameter = create();
            parameter.ParameterName = name;
            parameter.Value = value;
            parameters.Add(parameter);
        }

        private async Task WriteAutomationLogAsync(DateTimeOffset started) {
            try {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.AutomationLogs.Add(new AutomationLog { Type = AutomationLogType, StartTime = started, EndTime = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(CancellationToken.None);
            } catch(Exception e) {
                logger.LogError(e, "storage sweep could not write its AutomationLog row");
            }
        }
    }
}
