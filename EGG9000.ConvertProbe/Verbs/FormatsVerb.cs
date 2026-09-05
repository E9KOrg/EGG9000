using Npgsql;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public sealed class FormatsVerb {
        private sealed record Bucket(string Table, string Column, string Split, string Format, string Algo, long Rows, long TotalBytes, double AvgBytes, double P50, double P95);

        private static readonly string[] BucketHeaders = ["split", "format", "algo", "rows", "total bytes", "avg bytes", "p50 bytes", "p95 bytes"];
        private static readonly string[] ProgressHeaders = ["table", "column", "split", "rows", "envelope rows", "envelope row share", "bytes", "envelope bytes", "envelope byte share"];
        private static readonly string[] ResponseHeaders = ["table", "null", "non-null", "total bytes"];
        private static readonly string[] SizeHeaders = ["table", "pg_total_relation_size", "pg_relation_size", "blob column", "sum(pg_column_size)"];

        public static async Task<int> RunAsync(ProbeOptions options) {
            await using var connection = await options.OpenConnectionAsync();
            var users = await HistogramAsync(connection, Sql.UsersTable, Sql.UsersAccountsBlob, Sql.UsersReachSplit);
            var coops = await HistogramAsync(connection, Sql.CoopsTable, Sql.CoopsStatusBlob, Sql.CoopFinishedSplit);

            var output = new StringBuilder();
            output.Append(Markdown.Heading(2, "Context"));
            output.AppendLine(await QueryTableAsync(connection, Sql.Context, ["database", "server", "database size", "probe time (server now())", "backup as-of (max AutomationLogs.StartTime)", "user tables"]));
            output.Append(Markdown.Heading(2, $"{Sql.UsersTable}.{Sql.UsersAccountsBlob} by first byte (split by UpdateBackups reachability)"));
            output.AppendLine(BucketTable(users));
            output.Append(Markdown.Heading(2, $"{Sql.CoopsTable}.{Sql.CoopsStatusBlob} by first byte (split by ThreadsCoopStatusUpdater polling predicate)"));
            output.AppendLine(BucketTable(coops));
            output.Append(Markdown.Heading(2, $"{Sql.CoopsTable} by Status (polled = ThreadsCoopStatusUpdater predicate)"));
            output.AppendLine(await QueryTableAsync(connection, Sql.CoopsByStatus, ["status", "rows", "polled", "null blob", "blob bytes"]));
            output.Append(Markdown.Heading(2, $"{Sql.CoopsTable} by age (Created)"));
            output.AppendLine(await QueryTableAsync(connection, Sql.CoopsByAge, ["age", "rows", "polled", "blob bytes"]));
            output.Append(Markdown.Heading(2, "Conversion progress (envelope share)"));
            output.AppendLine(ProgressTable(users.Concat(coops)));
            output.Append(Markdown.Heading(2, $"{Sql.ResponseColumn} presence"));
            output.AppendLine(await ResponseTableAsync(connection));
            output.Append(Markdown.Heading(2, "Relation sizes"));
            output.AppendLine(await SizeTableAsync(connection));
            output.Append(Markdown.Heading(2, "pg_stat_user_tables (write counters for later deltas)"));
            output.AppendLine(await QueryTableAsync(connection, Sql.TableStats, ["table", "live", "dead", "ins", "upd", "hot upd", "del", "total size", "last autovacuum", "last autoanalyze"]));
            output.Append(Markdown.Heading(2, $"{Sql.AutomationLogsTable} last 7 days before backup as-of"));
            output.AppendLine(await QueryTableAsync(connection, Sql.AutomationSevenDays, ["type", "runs", "skipped", "avg s", "p95 s", "max s", "unfinished", "last start"]));
            Console.Write(output);

            if(options.Csv is not null) AppendCsv(options.Csv, users.Concat(coops));
            return 0;
        }

        private static async Task<List<Bucket>> HistogramAsync(NpgsqlConnection connection, string table, string column, string splitExpr) {
            var buckets = new List<Bucket>();
            await using var command = new NpgsqlCommand(Sql.Histogram(table, column, splitExpr), connection);
            await using var reader = await command.ExecuteReaderAsync();
            while(await reader.ReadAsync()) {
                var algo = reader.IsDBNull(2) ? "" : AccountDecoder.AlgoName((byte)reader.GetInt32(2));
                buckets.Add(new Bucket(table, column, reader.GetString(0), reader.GetString(1), algo,
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetDouble(7)));
            }
            return buckets;
        }

        private static string BucketTable(List<Bucket> buckets) {
            var rows = buckets.Select(b => (IReadOnlyList<string>)[
                b.Split, b.Format, b.Algo, Markdown.Num(b.Rows), Markdown.Bytes(b.TotalBytes),
                Markdown.Num(b.AvgBytes, 0), Markdown.Num(b.P50, 0), Markdown.Num(b.P95, 0)
            ]).ToList();
            var totalRows = buckets.Sum(b => b.Rows);
            var totalBytes = buckets.Sum(b => b.TotalBytes);
            rows.Add(["total", "", "", Markdown.Num(totalRows), Markdown.Bytes(totalBytes), totalRows == 0 ? "0" : Markdown.Num((double)totalBytes / totalRows, 0), "", ""]);
            return Markdown.Table(BucketHeaders, rows);
        }

        private static string ProgressTable(IEnumerable<Bucket> buckets) {
            var rows = buckets
                .GroupBy(b => (b.Table, b.Column, b.Split))
                .Select(g => {
                    var rowsTotal = g.Sum(b => b.Rows);
                    var bytesTotal = g.Sum(b => b.TotalBytes);
                    var envelopeRows = g.Where(b => b.Format == "envelope").Sum(b => b.Rows);
                    var envelopeBytes = g.Where(b => b.Format == "envelope").Sum(b => b.TotalBytes);
                    return (IReadOnlyList<string>)[
                        g.Key.Table, g.Key.Column, g.Key.Split, Markdown.Num(rowsTotal), Markdown.Num(envelopeRows), Markdown.Percent(envelopeRows, rowsTotal),
                        Markdown.Bytes(bytesTotal), Markdown.Bytes(envelopeBytes), Markdown.Percent(envelopeBytes, bytesTotal)
                    ];
                });
            return Markdown.Table(ProgressHeaders, rows);
        }

        private static async Task<string> ResponseTableAsync(NpgsqlConnection connection) {
            var rows = new List<IReadOnlyList<string>>();
            foreach(var table in Sql.ResponseTables) {
                await using var existsCommand = new NpgsqlCommand(Sql.ColumnExists(table, Sql.ResponseColumn), connection);
                if(await existsCommand.ExecuteScalarAsync() is not true) {
                    rows.Add([table, "absent (pre-migration)", "", ""]);
                    continue;
                }
                await using var command = new NpgsqlCommand(Sql.ResponseCounts(table), connection);
                await using var reader = await command.ExecuteReaderAsync();
                if(await reader.ReadAsync())
                    rows.Add([table, Markdown.Num(reader.GetInt64(0)), Markdown.Num(reader.GetInt64(1)), Markdown.Bytes(reader.GetInt64(2))]);
            }
            return Markdown.Table(ResponseHeaders, rows);
        }

        private static async Task<string> SizeTableAsync(NpgsqlConnection connection) {
            var blobColumns = new Dictionary<string, string> {
                [Sql.UsersTable] = Sql.UsersAccountsBlob,
                [Sql.CoopsTable] = Sql.CoopsStatusBlob
            };
            var rows = new List<IReadOnlyList<string>>();
            foreach(var table in Sql.SizedTables) {
                long total, heap;
                await using(var command = new NpgsqlCommand(Sql.RelationSizes(table), connection))
                await using(var reader = await command.ExecuteReaderAsync()) {
                    await reader.ReadAsync();
                    total = reader.GetInt64(0);
                    heap = reader.GetInt64(1);
                }
                var column = blobColumns.GetValueOrDefault(table, "");
                var columnSize = "";
                if(column.Length > 0) {
                    await using var command = new NpgsqlCommand(Sql.ColumnSize(table, column), connection);
                    columnSize = Markdown.Bytes(Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
                }
                rows.Add([table, Markdown.Bytes(total), Markdown.Bytes(heap), column, columnSize]);
            }
            return Markdown.Table(SizeHeaders, rows);
        }

        private static async Task<string> QueryTableAsync(NpgsqlConnection connection, string sql, string[] headers) {
            var rows = new List<IReadOnlyList<string>>();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while(await reader.ReadAsync()) {
                var cells = new string[reader.FieldCount];
                for(var i = 0; i < reader.FieldCount; i++)
                    cells[i] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                rows.Add(cells);
            }
            return Markdown.Table(headers, rows);
        }

        private static void AppendCsv(string path, IEnumerable<Bucket> buckets) {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if(!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
            using var writer = new StreamWriter(path, true, new UTF8Encoding(false));
            if(writeHeader)
                writer.WriteLine("timestamp_utc,table,column,split,format,algo,rows,total_bytes,avg_bytes,p50_bytes,p95_bytes");
            var stamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            foreach(var b in buckets)
                writer.WriteLine(Csv.Line(stamp, b.Table.Trim('"'), b.Column.Trim('"'), b.Split, b.Format, b.Algo,
                    Markdown.Num(b.Rows), Markdown.Num(b.TotalBytes), Markdown.Num(b.AvgBytes, 1), Markdown.Num(b.P50, 0), Markdown.Num(b.P95, 0)));
        }
    }
}
