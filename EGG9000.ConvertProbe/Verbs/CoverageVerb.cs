using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public sealed class CoverageVerb {
        private const string CsvHeader = "user_id,discord_id,format,algo,decode_ok,error_type,account_count,account_index,egg_inc_id,backup_present,ei_backup_present,farms,farms_with_simulation,farms_with_local_contract,archived_farms";

        public static async Task<int> RunAsync(ProbeOptions options) {
            var outDir = options.EnsureOutDir();
            var csvPath = Path.Combine(outDir, "coverage.csv");
            await using var db = options.CreateContext();
            using var csv = new StreamWriter(csvPath, false, new UTF8Encoding(false));
            csv.WriteLine(CsvHeader);

            long users = 0, decodeOk = 0, decodeFailed = 0;
            long accounts = 0, backupsPresent = 0, eiBackupsPresent = 0;
            long farms = 0, farmsWithSimulation = 0, farmsWithLocalContract = 0, archivedFarms = 0;
            var byFormat = new SortedDictionary<string, long>(StringComparer.Ordinal);
            var failures = new SortedDictionary<string, long>(StringComparer.Ordinal);
            var failureDetails = new List<IReadOnlyList<string>>();
            var stopwatch = Stopwatch.StartNew();

            await foreach(var user in AccountDecoder.StreamUsersAsync(db, options.Limit)) {
                users++;
                var format = AccountDecoder.FormatOf(user.Blob);
                var algo = AccountDecoder.AlgoOf(user.Blob);
                var formatKey = algo.Length == 0 ? format : format + "/" + algo;
                byFormat[formatKey] = byFormat.GetValueOrDefault(formatKey) + 1;

                var result = AccountDecoder.Decode(user.Blob);
                if(!result.Ok) {
                    decodeFailed++;
                    var errorType = result.Error.GetType().Name;
                    failures[errorType] = failures.GetValueOrDefault(errorType) + 1;
                    var root = result.Error;
                    while(root.InnerException is not null) root = root.InnerException;
                    var head = Convert.ToHexString(user.Blob.AsSpan(0, Math.Min(12, user.Blob.Length)));
                    failureDetails.Add([user.Id.ToString(), Markdown.Num(user.Blob.Length), head, errorType, Trim(result.Error.Message), root == result.Error ? "" : root.GetType().Name + ": " + Trim(root.Message)]);
                    csv.WriteLine(Csv.Line(user.Id.ToString(), user.DiscordId.ToString(), format, algo, "false", errorType, "", "", "", "", "", "", "", "", ""));
                    continue;
                }

                decodeOk++;
                accounts += result.Accounts.Count;
                for(var i = 0; i < result.Accounts.Count; i++) {
                    var account = result.Accounts[i];
                    var backup = account?.Backup;
                    var hasBackup = backup is not null;
                    var hasEiBackup = backup?.EiBackupBytes is { Length: > 0 };
                    var farmList = backup?.Farms ?? [];
                    var withSimulation = farmList.Count(f => f?.SimulationBytes is { Length: > 0 });
                    var withLocalContract = farmList.Count(f => f?.LocalContractBytes is { Length: > 0 });
                    var archived = backup?.ArchivedFarms?.Count ?? 0;

                    if(hasBackup) backupsPresent++;
                    if(hasEiBackup) eiBackupsPresent++;
                    farms += farmList.Count;
                    farmsWithSimulation += withSimulation;
                    farmsWithLocalContract += withLocalContract;
                    archivedFarms += archived;

                    csv.WriteLine(Csv.Line(user.Id.ToString(), user.DiscordId.ToString(), format, algo, "true", "",
                        result.Accounts.Count.ToString(), i.ToString(), account?.Id ?? "",
                        hasBackup ? "true" : "false", hasEiBackup ? "true" : "false",
                        farmList.Count.ToString(), withSimulation.ToString(), withLocalContract.ToString(), archived.ToString()));
                }
                if(users % 5000 == 0) Console.Error.WriteLine($"coverage: {users} users in {stopwatch.Elapsed:mm\\:ss}");
            }

            var output = new StringBuilder();
            output.Append(Markdown.Heading(2, "Users"));
            output.AppendLine(Markdown.Table(["metric", "value"], [
                ["users with accounts blob", Markdown.Num(users)],
                ["decoded ok", Markdown.Num(decodeOk)],
                ["decode failed", Markdown.Num(decodeFailed)],
                ["elapsed", stopwatch.Elapsed.ToString(@"mm\:ss")]
            ]));
            output.Append(Markdown.Heading(3, "Stored format"));
            output.AppendLine(Markdown.Table(["format", "users", "share"], byFormat.Select(kv => (IReadOnlyList<string>)[kv.Key, Markdown.Num(kv.Value), Markdown.Percent(kv.Value, users)])));
            if(failures.Count > 0) {
                output.Append(Markdown.Heading(3, "Decode failures"));
                output.AppendLine(Markdown.Table(["exception", "users"], failures.Select(kv => (IReadOnlyList<string>)[kv.Key, Markdown.Num(kv.Value)])));
                output.Append(Markdown.Heading(3, "Decode failure detail"));
                output.AppendLine(Markdown.Table(["user id", "blob bytes", "first bytes (hex)", "exception", "message", "root cause"], failureDetails));
            }
            output.Append(Markdown.Heading(2, "Accounts"));
            output.AppendLine(Markdown.Table(["metric", "count", "share"], [
                ["accounts", Markdown.Num(accounts), ""],
                ["with Backup", Markdown.Num(backupsPresent), Markdown.Percent(backupsPresent, accounts)],
                ["with EiBackupBytes", Markdown.Num(eiBackupsPresent), Markdown.Percent(eiBackupsPresent, backupsPresent)],
                ["farms", Markdown.Num(farms), ""],
                ["farms with SimulationBytes", Markdown.Num(farmsWithSimulation), Markdown.Percent(farmsWithSimulation, farms)],
                ["farms with LocalContractBytes", Markdown.Num(farmsWithLocalContract), Markdown.Percent(farmsWithLocalContract, farms)],
                ["archived farms", Markdown.Num(archivedFarms), ""]
            ]));
            output.AppendLine($"CSV: {csvPath}");
            Console.Write(output);
            return decodeFailed > 0 ? 2 : 0;
        }

        private static string Trim(string message) {
            var single = (message ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
            return single.Length <= 220 ? single : single[..220] + "...";
        }
    }
}
