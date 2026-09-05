using EGG9000.Common.Database.Entities;

namespace EGG9000.ConvertProbe {
    public static class Sql {
        public const string UsersTable = "\"Users\"";
        public const string UsersId = "\"Id\"";
        public const string UsersDiscordId = "\"DiscordId\"";
        public const string UsersAccountsBlob = "\"_contractRegistrationByte\"";
        public const string UsersStaleBackup = "\"StaleBackup\"";
        public const string UsersGuildId = "\"GuildId\"";
        public const string UsersTempDisabled = "\"TempDisabled\"";
        public const string UsersLastBackupCheck = "\"LastBackupCheck\"";

        public const string CoopsTable = "\"Coops\"";
        public const string CoopsStatusBlob = "\"_StatusCompressed\"";
        public const string CoopsStatus = "\"Status\"";
        public const string CoopsCreated = "\"Created\"";
        public const string CoopsCreatorId = "\"CreatorID\"";
        public const string CoopsThreadId = "\"ThreadID\"";
        public const string CoopsThreadArchived = "\"ThreadArchived\"";
        public const string CoopsCoopEnds = "\"CoopEnds\"";

        public const string ContractsTable = "\"Contracts\"";
        public const string ContractsId = "\"ID\"";
        public const string GuildsTable = "\"Guilds\"";
        public const string GuildsId = "\"Id\"";
        public const string CustomEggsTable = "\"CustomEggs\"";
        public const string EventsTable = "\"Events\"";
        public const string SeasonInfosTable = "\"SeasonInfos\"";
        public const string ResponseColumn = "\"_response\"";

        public const string AutomationLogsTable = "\"AutomationLogs\"";
        public const string AutomationLogsType = "\"Type\"";
        public const string AutomationLogsStartTime = "\"StartTime\"";
        public const string AutomationLogsEndTime = "\"EndTime\"";
        public const string AutomationLogsSkipped = "\"Skipped\"";

        public const string FinishedCoopStatuses = "(14, 15, -1)";
        public const string CoopPolledPredicate = CoopsThreadId + " <> 0 AND NOT " + CoopsThreadArchived + " AND " + CoopsCoopEnds + " IS NOT NULL AND " + CoopsCoopEnds + " + interval '7 days' > now()";
        public const string CoopFinishedSplit = "CASE WHEN " + CoopPolledPredicate + " THEN 'polled' ELSE 'not polled' END";
        public const string UsersUnknownGuildPredicate = UsersGuildId + " <> 0 AND NOT EXISTS (SELECT 1 FROM " + GuildsTable + " g WHERE g." + GuildsId + " = u." + UsersGuildId + ")";
        public const string UsersReachSplit = "CASE WHEN " + UsersTempDisabled + " THEN 'unreachable: disabled' WHEN " + UsersGuildId + " = 0 THEN 'unreachable: no guild' WHEN " + UsersUnknownGuildPredicate + " THEN 'unreachable: unknown guild' WHEN " + UsersStaleBackup + " THEN 'stale' ELSE 'fresh' END";

        public const string Context = """
            SELECT current_database(), version(), pg_size_pretty(pg_database_size(current_database())), now()::text,
                   (SELECT max("StartTime") FROM "AutomationLogs")::text,
                   (SELECT count(*) FROM pg_stat_user_tables)::text
            """;

        public const string TableStats = """
            SELECT relname, n_live_tup, n_dead_tup, n_tup_ins, n_tup_upd, n_tup_hot_upd, n_tup_del,
                   pg_size_pretty(pg_total_relation_size(relid)), coalesce(last_autovacuum::text, ''), coalesce(last_autoanalyze::text, '')
            FROM pg_stat_user_tables
            ORDER BY pg_total_relation_size(relid) DESC
            """;

        public static string CoopsByStatus => $"""
            SELECT {CoopsStatus}, count(*), count(*) FILTER (WHERE {CoopPolledPredicate}), count(*) FILTER (WHERE {CoopsStatusBlob} IS NULL),
                   pg_size_pretty(coalesce(sum(octet_length({CoopsStatusBlob})), 0))
            FROM {CoopsTable}
            GROUP BY {CoopsStatus}
            ORDER BY {CoopsStatus}
            """;

        public static string CoopsByAge => $"""
            SELECT age, count(*), count(*) FILTER (WHERE polled), pg_size_pretty(coalesce(sum(bytes), 0))
            FROM (
                SELECT CASE WHEN {CoopsCreated} > now() - interval '7 days' THEN '1: 0-7d'
                            WHEN {CoopsCreated} > now() - interval '30 days' THEN '2: 7-30d'
                            WHEN {CoopsCreated} > now() - interval '90 days' THEN '3: 30-90d'
                            WHEN {CoopsCreated} > now() - interval '365 days' THEN '4: 90-365d'
                            ELSE '5: over 1y' END AS age,
                       ({CoopPolledPredicate}) AS polled,
                       octet_length({CoopsStatusBlob}) AS bytes
                FROM {CoopsTable}
            ) s
            GROUP BY age
            ORDER BY age
            """;

        public static string AutomationSevenDays => $"""
            SELECT {AutomationLogsType}, count(*), count(*) FILTER (WHERE {AutomationLogsSkipped}),
                   round(avg(extract(epoch FROM ({AutomationLogsEndTime} - {AutomationLogsStartTime})))::numeric, 1),
                   round((percentile_cont(0.95) WITHIN GROUP (ORDER BY extract(epoch FROM ({AutomationLogsEndTime} - {AutomationLogsStartTime}))))::numeric, 1),
                   round(max(extract(epoch FROM ({AutomationLogsEndTime} - {AutomationLogsStartTime})))::numeric, 1),
                   count(*) FILTER (WHERE {AutomationLogsEndTime} IS NULL AND NOT {AutomationLogsSkipped}),
                   max({AutomationLogsStartTime})::text
            FROM {AutomationLogsTable}
            WHERE {AutomationLogsStartTime} > (SELECT max({AutomationLogsStartTime}) FROM {AutomationLogsTable}) - interval '7 days'
            GROUP BY {AutomationLogsType}
            ORDER BY {AutomationLogsType}
            """;

        public static readonly string[] ResponseTables = [ContractsTable, CustomEggsTable, EventsTable, SeasonInfosTable];
        public static readonly string[] SizedTables = [UsersTable, CoopsTable, ContractsTable, CustomEggsTable, EventsTable, SeasonInfosTable];

        public static string Histogram(string table, string column, string splitExpr) => $"""
            SELECT split, fmt, algo,
                   count(*)::bigint AS row_count,
                   coalesce(sum(len), 0)::bigint AS total_bytes,
                   coalesce(avg(len), 0)::float8 AS avg_bytes,
                   coalesce(percentile_cont(0.5) WITHIN GROUP (ORDER BY len), 0)::float8 AS p50_bytes,
                   coalesce(percentile_cont(0.95) WITHIN GROUP (ORDER BY len), 0)::float8 AS p95_bytes
            FROM (
                SELECT {splitExpr} AS split,
                       octet_length({column}) AS len,
                       CASE WHEN octet_length({column}) = 0 THEN 'empty'
                            WHEN get_byte({column}, 0) = 235 THEN 'envelope'
                            WHEN get_byte({column}, 0) = 31 THEN 'gzip'
                            ELSE 'legacy' END AS fmt,
                       CASE WHEN octet_length({column}) >= 2 AND get_byte({column}, 0) = 235 THEN get_byte({column}, 1) END AS algo
                FROM {table} u
                WHERE {column} IS NOT NULL
            ) s
            GROUP BY split, fmt, algo
            ORDER BY split, fmt, algo
            """;

        public static string ColumnExists(string table, string column) =>
            $"SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table.Trim('"')}' AND column_name = '{column.Trim('"')}')";

        public static string ResponseCounts(string table) => $"""
            SELECT count(*) FILTER (WHERE {ResponseColumn} IS NULL)::bigint AS null_rows,
                   count(*) FILTER (WHERE {ResponseColumn} IS NOT NULL)::bigint AS non_null_rows,
                   coalesce(sum(octet_length({ResponseColumn})), 0)::bigint AS total_bytes
            FROM {table}
            """;

        public static string RelationSizes(string table) => $"SELECT pg_total_relation_size('{table}')::bigint, pg_relation_size('{table}')::bigint";

        public static string ColumnSize(string table, string column) => $"SELECT coalesce(sum(pg_column_size({column})), 0)::bigint FROM {table}";

        public static string ContractResponses => $"SELECT {ContractsId}, {ResponseColumn} FROM {ContractsTable}";

        public static string RandomAccountBlobs => $"""
            SELECT {UsersAccountsBlob}
            FROM {UsersTable}
            WHERE {UsersAccountsBlob} IS NOT NULL AND octet_length({UsersAccountsBlob}) > 0
            ORDER BY random()
            LIMIT @limit
            """;

        public static string PreferActiveCoopBlobs => $"""
            SELECT {CoopsStatusBlob}
            FROM {CoopsTable}
            WHERE {CoopsStatusBlob} IS NOT NULL AND octet_length({CoopsStatusBlob}) > 0
              AND {CoopsCreatorId} IS DISTINCT FROM '{Coop.TestSeedCreatorId}'
            ORDER BY CASE WHEN {CoopPolledPredicate} THEN 0 ELSE 1 END, random()
            LIMIT @limit
            """;
    }
}
