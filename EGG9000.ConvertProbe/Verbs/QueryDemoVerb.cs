using EGG9000.Common.Database.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public sealed class QueryDemoVerb {
        public const string FieldPath = "Backup.EiBackup.Game.LifetimeCashEarned";

        public static async Task<int> RunAsync(ProbeOptions options) {
            await using var db = options.CreateContext();
            long users = 0, decodeFailed = 0, accounts = 0, withBackup = 0, withEiBackup = 0, parseFailed = 0;
            var values = new List<(string EggIncId, double Value)>();

            await foreach(var user in AccountDecoder.StreamUsersAsync(db, options.Limit)) {
                users++;
                var result = AccountDecoder.Decode(user.Blob);
                if(!result.Ok) {
                    decodeFailed++;
                    continue;
                }
                accounts += result.Accounts.Count;
                withBackup += result.Accounts.Count(a => a?.Backup is not null);
                foreach(var account in result.Accounts.Where(a => a?.Backup?.EiBackupBytes is { Length: > 0 })) {
                    var game = TryGame(account);
                    if(game is null) {
                        parseFailed++;
                        continue;
                    }
                    withEiBackup++;
                    values.Add((account.Id ?? "", game.LifetimeCashEarned));
                }
            }

            var output = new StringBuilder();
            output.Append(Markdown.Heading(2, "Query demo"));
            output.AppendLine($"Field: `{FieldPath}` (Ei.Backup.Game is a kept subtree; LifetimeCashEarned has no CustomBackup member and was never projected by any legacy key).");
            output.AppendLine();
            output.AppendLine(Markdown.Table(["metric", "value"], [
                ["users scanned", Markdown.Num(users)],
                ["decode failures", Markdown.Num(decodeFailed)],
                ["accounts", Markdown.Num(accounts)],
                ["accounts with Backup", Markdown.Num(withBackup)],
                ["accounts with EiBackup", Markdown.Num(withEiBackup)],
                ["EiBackup parse failures", Markdown.Num(parseFailed)],
                ["queryable share of backups", Markdown.Percent(withEiBackup, withBackup)]
            ]));

            if(values.Count == 0) {
                output.AppendLine("No accounts carry EiBackupBytes yet; nothing to query.");
                Console.Write(output);
                return 0;
            }

            var sorted = values.Select(v => v.Value).OrderBy(v => v).ToList();
            output.Append(Markdown.Heading(3, "Distribution"));
            output.AppendLine(Markdown.Table(["stat", "value"], [
                ["min", Sci(sorted[0])],
                ["median", Sci(sorted[sorted.Count / 2])],
                ["avg", Sci(sorted.Average())],
                ["max", Sci(sorted[^1])],
                ["non-zero", Markdown.Num(sorted.Count(v => v > 0))]
            ]));
            output.Append(Markdown.Heading(3, "Top 10"));
            output.AppendLine(Markdown.Table(["egg inc id", "lifetime cash earned"],
                values.OrderByDescending(v => v.Value).Take(10).Select(v => (IReadOnlyList<string>)[Mask(v.EggIncId), Sci(v.Value)])));
            Console.Write(output);
            return 0;
        }

        private static Ei.Backup.Types.Game TryGame(EggIncAccount account) {
            try {
                return account.Backup.EiBackup?.Game;
            } catch(Exception) {
                return null;
            }
        }

        private static string Sci(double value) => value.ToString("0.###E+0", System.Globalization.CultureInfo.InvariantCulture);

        private static string Mask(string id) {
            if(id.Length <= 7) return id;
            return id[..4] + "..." + id[^3..];
        }
    }
}
