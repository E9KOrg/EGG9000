using EGG9000.Common.Database.Entities;
using EGG9000.ConvertProbe.Inspect;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public sealed partial class InspectVerb {
        private const string CsvHeader = "user_id,discord_id,format,framing,decode_ok,decode_root_cause,account_index,path,member,kind,declared,msgpack_type,raw_value";
        private const int DetailRowCap = 500;

        private sealed record UserRow(Guid Id, string Framing, string RootCause, int Problems, int Total, string WalkNote);

        public static async Task<int> RunAsync(ProbeOptions options) {
            var outDir = options.EnsureOutDir();
            var csvPath = Path.Combine(outDir, "inspect.csv");
            await using var db = options.CreateContext();
            using var csv = new StreamWriter(csvPath, false, new UTF8Encoding(false));
            csv.WriteLine(CsvHeader);

            var builder = new SlotSchemaBuilder();
            var walker = new SchemaWalker(builder.Build(typeof(EggIncAccount)));

            long users = 0, decodeFailed = 0, inspected = 0, usersWithProblems = 0;
            var failingWithoutProblem = new List<Guid>();
            var userRows = new List<UserRow>();
            var byPattern = new Dictionary<(string Pattern, string Kind, string Declared), (long Count, HashSet<Guid> Users)>();
            var detail = new List<IReadOnlyList<string>>();
            long detailOmitted = 0;
            var stopwatch = Stopwatch.StartNew();

            await foreach(var user in AccountDecoder.StreamUsersAsync(db, options.Limit)) {
                users++;
                var format = AccountDecoder.FormatOf(user.Blob);
                var result = AccountDecoder.Decode(user.Blob);
                var rootCause = "";
                if(!result.Ok) {
                    decodeFailed++;
                    var root = result.Error;
                    while(root.InnerException is not null) root = root.InnerException;
                    rootCause = root.GetType().Name + ": " + Trim(root.Message);
                } else if(!options.All) {
                    if(users % 5000 == 0) Console.Error.WriteLine($"inspect: {users} users in {stopwatch.Elapsed:mm\\:ss}");
                    continue;
                }

                inspected++;
                string framing;
                List<Finding> findings;
                var walkNote = "";
                try {
                    var plain = BlobDecompressor.ToPlainMessagePack(user.Blob);
                    framing = plain.Framing;
                    findings = walker.Walk(plain.Plain);
                } catch(Exception e) {
                    framing = "?";
                    findings = [];
                    walkNote = "decompress failed: " + e.GetType().Name + ": " + Trim(e.Message);
                }

                var problems = findings.Count(f => f.IsProblem);
                if(problems > 0) usersWithProblems++;
                if(!result.Ok && problems == 0) failingWithoutProblem.Add(user.Id);
                userRows.Add(new UserRow(user.Id, framing, rootCause, problems, findings.Count, walkNote));

                if(findings.Count == 0)
                    csv.WriteLine(Csv.Line(user.Id.ToString(), user.DiscordId.ToString(), format, framing, result.Ok ? "true" : "false", rootCause, "", "", "", walkNote.Length == 0 ? "" : FindingKind.WalkError, "", "", walkNote));
                foreach(var finding in findings) {
                    csv.WriteLine(Csv.Line(user.Id.ToString(), user.DiscordId.ToString(), format, framing, result.Ok ? "true" : "false", rootCause,
                        finding.AccountIndex.ToString(), finding.Path, finding.Member, finding.Kind, finding.Declared, finding.MsgpackType, finding.RawValue));
                    var patternKey = (Pattern(finding.Path), finding.Kind, finding.Declared);
                    if(!byPattern.TryGetValue(patternKey, out var bucket)) bucket = (0, []);
                    bucket.Users.Add(user.Id);
                    byPattern[patternKey] = (bucket.Count + 1, bucket.Users);
                    if(!finding.IsProblem) continue;
                    if(detail.Count >= DetailRowCap) {
                        detailOmitted++;
                        continue;
                    }
                    detail.Add([user.Id.ToString(), finding.AccountIndex.ToString(), finding.Path, finding.Member, finding.Kind, finding.Declared, finding.MsgpackType, Markdown.Clip(finding.RawValue, 80)]);
                }
                if(users % 5000 == 0) Console.Error.WriteLine($"inspect: {users} users in {stopwatch.Elapsed:mm\\:ss}");
            }

            var output = new StringBuilder();
            output.Append(Markdown.Heading(2, "Schema"));
            output.AppendLine(Markdown.Table(["type", "declared keys", "max key", "skipped"],
                builder.ObjectSchemas().OrderBy(s => s.Name, StringComparer.Ordinal)
                    .Select(s => (IReadOnlyList<string>)[s.Name, Markdown.Num(s.Members.Count), Markdown.Num(s.MaxKey), s.Skipped ?? ""])));
            if(builder.Notes.Count > 0) {
                foreach(var note in builder.Notes.Distinct(StringComparer.Ordinal))
                    output.AppendLine("- " + note);
                output.AppendLine();
            }

            output.Append(Markdown.Heading(2, "Users"));
            output.AppendLine(Markdown.Table(["metric", "value"], [
                ["users with accounts blob", Markdown.Num(users)],
                ["decode failed", Markdown.Num(decodeFailed)],
                ["inspected", Markdown.Num(inspected)],
                ["inspected with problems", Markdown.Num(usersWithProblems)],
                ["decode failed without a finding", Markdown.Num(failingWithoutProblem.Count)],
                ["elapsed", stopwatch.Elapsed.ToString(@"mm\:ss")]
            ]));
            if(userRows.Count > 0) {
                output.Append(Markdown.Heading(3, "Inspected users"));
                var shown = userRows.Where(r => r.RootCause.Length > 0 || r.Problems > 0 || r.WalkNote.Length > 0).Take(DetailRowCap).ToList();
                output.AppendLine(Markdown.Table(["user id", "framing", "decode root cause", "problems", "all findings", "walk note"],
                    shown.Select(r => (IReadOnlyList<string>)[r.Id.ToString(), r.Framing, r.RootCause, Markdown.Num(r.Problems), Markdown.Num(r.Total), r.WalkNote])));
                if(shown.Count < userRows.Count)
                    output.AppendLine($"{userRows.Count - shown.Count} inspected users with no decode failure and no problem findings omitted.").AppendLine();
            }

            if(byPattern.Count > 0) {
                output.Append(Markdown.Heading(2, "Findings by path"));
                output.AppendLine(Markdown.Table(["path", "kind", "declared", "occurrences", "users"],
                    byPattern.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key.Pattern, StringComparer.Ordinal)
                        .Select(kv => (IReadOnlyList<string>)[kv.Key.Pattern, kv.Key.Kind, kv.Key.Declared, Markdown.Num(kv.Value.Count), Markdown.Num(kv.Value.Users.Count)])));
            }
            if(detail.Count > 0) {
                output.Append(Markdown.Heading(2, "Problem findings"));
                output.AppendLine(Markdown.Table(["user id", "account", "path", "member", "kind", "declared", "msgpack", "raw value"], detail));
                if(detailOmitted > 0)
                    output.AppendLine($"{detailOmitted} further problem findings omitted from this table; see CSV.").AppendLine();
            }
            output.AppendLine($"CSV: {csvPath}");
            Console.Write(output);
            return decodeFailed > 0 ? 2 : 0;
        }

        private static string Pattern(string path) => MapIndex().Replace(ListIndex().Replace(path, "[]"), "[#]");

        private static string Trim(string message) {
            var single = (message ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
            return single.Length <= 220 ? single : single[..220] + "...";
        }

        [GeneratedRegex(@"\[\d+\]")]
        private static partial Regex ListIndex();

        [GeneratedRegex(@"\[#\d+\]")]
        private static partial Regex MapIndex();
    }
}
