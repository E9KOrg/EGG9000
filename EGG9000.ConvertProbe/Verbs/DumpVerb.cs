using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public sealed class DumpVerb {
        public static async Task<int> RunAsync(ProbeOptions options) {
            var dumpDir = Path.Combine(options.EnsureOutDir(), "dump");
            Directory.CreateDirectory(dumpDir);
            await using var db = options.CreateContext();
            var encoding = new UTF8Encoding(false);
            long users = 0, written = 0, errors = 0, decodeFailures = 0, withoutBackup = 0;
            var stopwatch = Stopwatch.StartNew();

            await foreach(var user in AccountDecoder.StreamUsersAsync(db, options.Limit)) {
                users++;
                var result = AccountDecoder.Decode(user.Blob);
                if(!result.Ok) {
                    decodeFailures++;
                    File.WriteAllText(Path.Combine(dumpDir, $"{user.DiscordId}_decode.error.txt"), result.Error.ToString(), encoding);
                    continue;
                }
                for(var i = 0; i < result.Accounts.Count; i++) {
                    var account = result.Accounts[i];
                    if(account?.Backup is null) {
                        withoutBackup++;
                        continue;
                    }
                    var name = $"{user.DiscordId}_account{i}";
                    try {
                        var eggIncId = string.IsNullOrEmpty(account.Id) ? account.Backup.EggIncId : account.Id;
                        if(!string.IsNullOrEmpty(eggIncId)) name = $"{user.DiscordId}_{SafeName(eggIncId)}";
                        File.WriteAllText(Path.Combine(dumpDir, name + ".json"), CanonicalJson.Serialize(account.Backup), encoding);
                        written++;
                    } catch(Exception e) {
                        errors++;
                        File.WriteAllText(Path.Combine(dumpDir, name + ".error.txt"), e.ToString(), encoding);
                    }
                }
                if(users % 2000 == 0) Console.Error.WriteLine($"dump: {users} users, {written} files in {stopwatch.Elapsed:mm\\:ss}");
            }

            Console.Write(Markdown.Heading(2, "Dump"));
            Console.WriteLine(Markdown.Table(["metric", "value"], [
                ["users", Markdown.Num(users)],
                ["decode failures", Markdown.Num(decodeFailures)],
                ["accounts without Backup", Markdown.Num(withoutBackup)],
                ["json files written", Markdown.Num(written)],
                ["serialization errors", Markdown.Num(errors)],
                ["directory", dumpDir],
                ["elapsed", stopwatch.Elapsed.ToString(@"mm\:ss")]
            ]));
            return 0;
        }

        private static string SafeName(string value) {
            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach(var c in value)
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return builder.ToString();
        }
    }
}
