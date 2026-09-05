using EGG9000.Common.Database.Entities;
using Newtonsoft.Json;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public sealed class GoalSetsVerb {
        private const string CsvHeader = "discord_id,egg_inc_id,farm_kind,contract_id,league,num_goals_achieved,stored_completed,new_completed";

        private sealed record GoalSet(int Goals, int Pe);

        private sealed class ContractGoals {
            public string Id { get; init; }
            public int GradeSpecs { get; init; }
            public int Goals { get; init; }
            public List<GoalSet> Sets { get; init; }
            public bool SetsDiffer => Sets.Skip(1).Any(s => s.Goals != Sets[0].Goals);
        }

        private sealed record ContractLoad(Dictionary<string, ContractGoals> Contracts, long Rows, long NullResponse, long ParseFailed);

        private sealed class FarmTotals(string kind) {
            public string Kind { get; } = kind;
            public long Scanned;
            public long Ungraded;
            public long ContractMissing;
            public long Affected;
            public long TrueToFalse;
            public long FalseToTrue;
        }

        public static async Task<int> RunAsync(ProbeOptions options) {
            var outDir = options.EnsureOutDir();
            var csvPath = Path.Combine(outDir, "goalsets.csv");
            var stopwatch = Stopwatch.StartNew();
            var load = await LoadContractsAsync(options);
            Console.Error.WriteLine($"goalsets: {load.Contracts.Count} contracts parsed in {stopwatch.Elapsed:mm\\:ss}");

            var archived = new FarmTotals("archived");
            var active = new FarmTotals("active");
            var affectedUsers = new HashSet<ulong>();
            long users = 0, decodeFailed = 0, accounts = 0, accountFailed = 0;

            await using var db = options.CreateContext();
            using var csv = new StreamWriter(csvPath, false, new UTF8Encoding(false));
            csv.WriteLine(CsvHeader);
            await foreach(var user in AccountDecoder.StreamUsersAsync(db, options.Limit)) {
                users++;
                var result = AccountDecoder.Decode(user.Blob);
                if(!result.Ok) {
                    decodeFailed++;
                    continue;
                }
                foreach(var account in result.Accounts) {
                    accounts++;
                    try {
                        ScanAccount(account, user, load.Contracts, archived, active, affectedUsers, csv);
                    } catch(Exception) {
                        accountFailed++;
                    }
                }
                if(users % 5000 == 0) Console.Error.WriteLine($"goalsets: {users} users in {stopwatch.Elapsed:mm\\:ss}");
            }

            var output = new StringBuilder();
            output.Append(Markdown.Heading(2, "Contracts"));
            output.AppendLine("Rule under test: ungraded archived farms compute Completed from GoalSets[League] (stack, Contract.GetGoals) instead of GoalSets[0] (master). Only contracts with no GradeSpecs and GoalSets whose goal counts differ between sets can change the result.");
            output.AppendLine();
            output.AppendLine(Markdown.Table(["metric", "value"], [
                ["rows", Markdown.Num(load.Rows)],
                ["parsed", Markdown.Num(load.Contracts.Count)],
                ["null _response", Markdown.Num(load.NullResponse)],
                ["parse failed", Markdown.Num(load.ParseFailed)],
                ["with GradeSpecs", Markdown.Num(load.Contracts.Values.Count(c => c.GradeSpecs > 0))],
                ["with GoalSets", Markdown.Num(load.Contracts.Values.Count(c => c.Sets.Count > 0))],
                ["GoalSets goal counts differ from set 0", Markdown.Num(load.Contracts.Values.Count(c => c.SetsDiffer))],
                ["of those, no GradeSpecs", Markdown.Num(load.Contracts.Values.Count(c => c.SetsDiffer && c.GradeSpecs == 0))]
            ]));
            var differing = load.Contracts.Values.Where(c => c.SetsDiffer).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            if(differing.Count > 0) {
                output.Append(Markdown.Heading(3, "Contracts whose GoalSets have differing goal counts"));
                output.AppendLine(Markdown.Table(["contract", "GradeSpecs", "GoalSets", "Goals (top-level)", "per set (goals / PE)"],
                    differing.Select(c => (IReadOnlyList<string>)[
                        c.Id, Markdown.Num(c.GradeSpecs), Markdown.Num(c.Sets.Count), Markdown.Num(c.Goals),
                        string.Join("; ", c.Sets.Select((s, i) => $"[{i}] {s.Goals} / {s.Pe}"))
                    ])));
            }

            output.Append(Markdown.Heading(2, "Farms"));
            output.AppendLine(Markdown.Table(["metric", "archived", "active"], [
                Row("scanned", archived.Scanned, active.Scanned),
                Row("Grade unset and League >= 1", archived.Ungraded, active.Ungraded),
                Row("contract not in Contracts", archived.ContractMissing, active.ContractMissing),
                Row("affected (no GradeSpecs, GoalSets[League] count differs from [0])", archived.Affected, active.Affected),
                Row("would flip true -> false", archived.TrueToFalse, active.TrueToFalse),
                Row("would flip false -> true", archived.FalseToTrue, active.FalseToTrue)
            ]));
            output.AppendLine(Markdown.Table(["metric", "value"], [
                ["users scanned", Markdown.Num(users)],
                ["decode failed", Markdown.Num(decodeFailed)],
                ["accounts", Markdown.Num(accounts)],
                ["accounts failed during scan", Markdown.Num(accountFailed)],
                ["distinct users affected", Markdown.Num(affectedUsers.Count)],
                ["elapsed", stopwatch.Elapsed.ToString(@"mm\:ss")]
            ]));
            output.AppendLine($"CSV: {csvPath}");
            Console.Write(output);
            return 0;
        }

        private static IReadOnlyList<string> Row(string metric, long archived, long active) => [metric, Markdown.Num(archived), Markdown.Num(active)];

        private static async Task<ContractLoad> LoadContractsAsync(ProbeOptions options) {
            var contracts = new Dictionary<string, ContractGoals>(StringComparer.Ordinal);
            long rows = 0, nullResponse = 0, parseFailed = 0;
            await using var connection = await options.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(Sql.ContractResponses, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while(await reader.ReadAsync()) {
                rows++;
                var id = reader.GetString(0);
                if(reader.IsDBNull(1)) {
                    nullResponse++;
                    continue;
                }
                Ei.Contract contract;
                try {
                    contract = JsonConvert.DeserializeObject<Ei.Contract>(reader.GetString(1));
                } catch(Exception) {
                    parseFailed++;
                    continue;
                }
                if(contract is null) {
                    nullResponse++;
                    continue;
                }
                contracts[id] = new ContractGoals {
                    Id = id,
                    GradeSpecs = contract.GradeSpecs.Count,
                    Goals = contract.Goals.Count,
                    Sets = [.. contract.GoalSets.Select(s => new GoalSet(s.Goals.Count, PeSum(s.Goals)))]
                };
            }
            return new ContractLoad(contracts, rows, nullResponse, parseFailed);
        }

        private static int PeSum(IEnumerable<Ei.Contract.Types.Goal> goals) =>
            (int)goals.Where(g => g.RewardType == Ei.RewardType.EggsOfProphecy).Sum(g => g.RewardAmount);

        private static void ScanAccount(EggIncAccount account, AccountDecoder.UserBlob user, Dictionary<string, ContractGoals> contracts, FarmTotals archived, FarmTotals active, HashSet<ulong> affectedUsers, StreamWriter csv) {
            var backup = account?.Backup;
            if(backup is null) return;
            foreach(var farm in backup.ArchivedFarms ?? [])
                if(farm is not null)
                    Evaluate(archived, contracts, affectedUsers, csv, user, account.Id, farm.ContractId, farm.Grade, farm.League, farm.NumGoalsAchieved, farm.Completed);
            foreach(var farm in backup.Farms ?? [])
                if(farm is not null)
                    Evaluate(active, contracts, affectedUsers, csv, user, account.Id, farm.ContractId, farm.Grade, farm.League, farm.NumGoalsAchieved, farm.Completed);
        }

        private static void Evaluate(FarmTotals totals, Dictionary<string, ContractGoals> contracts, HashSet<ulong> affectedUsers, StreamWriter csv, AccountDecoder.UserBlob user, string eggIncId, string contractId, Ei.Contract.Types.PlayerGrade grade, uint? league, byte numGoalsAchieved, bool storedCompleted) {
            totals.Scanned++;
            if(grade != Ei.Contract.Types.PlayerGrade.GradeUnset || league is not >= 1) return;
            totals.Ungraded++;
            if(contractId is null || !contracts.TryGetValue(contractId, out var contract)) {
                totals.ContractMissing++;
                return;
            }
            var index = (int)league.Value;
            if(contract.GradeSpecs != 0 || contract.Sets.Count <= index || contract.Sets[index].Goals == contract.Sets[0].Goals) return;
            totals.Affected++;
            var newCompleted = numGoalsAchieved == contract.Sets[index].Goals;
            if(storedCompleted && !newCompleted) totals.TrueToFalse++;
            if(!storedCompleted && newCompleted) totals.FalseToTrue++;
            affectedUsers.Add(user.DiscordId);
            csv.WriteLine(Csv.Line(MaskDiscord(user.DiscordId), MaskEggInc(eggIncId), totals.Kind, contractId, index.ToString(), numGoalsAchieved.ToString(),
                storedCompleted ? "true" : "false", newCompleted ? "true" : "false"));
        }

        private static string MaskDiscord(ulong id) {
            var text = id.ToString();
            return text.Length <= 4 ? text : "..." + text[^4..];
        }

        private static string MaskEggInc(string id) {
            id ??= "";
            return id.Length <= 7 ? id : id[..4] + "..." + id[^3..];
        }
    }
}
