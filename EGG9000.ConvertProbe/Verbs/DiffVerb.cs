using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public sealed class DiffVerb {
        private const string FarmsPath = "/Farms";
        private const string ArchivedFarmsPath = "/ArchivedFarms";
        private const int TopUnexplained = 20;
        private const int MaxTemplateRows = 200;

        private static readonly string[] VolatileNames = [
            "LastBackupTime", "TotalCS", "SeasonCS", "SubscriptionEnds", "SubscriptionLevel", "CacheAdded",
            "LastStepTime", "NumChickens", "CashEarned", "CashSpent", "EggsPaidFor", "GoldenEggsEarned", "GoldenEggsSpent",
            "PiggyBank", "SoulEggs", "CurrentMultiplier", "NumPrestiges", "NumDailyGiftsCollected", "NumPiggyBreaks",
            "CraftingXP", "VirtueEggsDelivered", "EovEarned", "Resets", "ShiftCount", "MaxFarmSizeReached",
            "CustomEggMaxFarmSizeReached", "SpaceMissions", "FuelingMission", "FuelAmounts", "ShipsSent", "ArtifactHall",
            "ArtifactSets", "Artifacts", "TankLevel", "EggMedalLevel", "Habs", "Vehicles", "TrainLength", "SilosOwned",
            "CommonResearch", "EpicResearch", "PermitLevel", "EggsOfProphecy", "MaxEggReached"
        ];
        private static readonly string[] VolatilePrefixes = ["BoostTokens", "DroneTakedowns"];

        private sealed record Diff(string File, string Path, string Kind, string Before, string After);

        private sealed class TemplateCounts {
            public long Volatile;
            public long Unexplained;
            public Diff Example;
        }

        public static Task<int> RunAsync(ProbeOptions options) {
            if(options.Positional.Count < 2) {
                Console.Error.WriteLine("diff requires <dirA> <dirB>.");
                return Task.FromResult(1);
            }
            var dirA = options.Positional[0];
            var dirB = options.Positional[1];
            if(!Directory.Exists(dirA) || !Directory.Exists(dirB)) {
                Console.Error.WriteLine("Both diff directories must exist.");
                return Task.FromResult(1);
            }
            var patterns = BuildPatterns(options.Volatile);
            var outDir = options.EnsureOutDir();

            var filesA = Directory.GetFiles(dirA, "*.json").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var filesB = Directory.GetFiles(dirB, "*.json").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var common = filesA.Intersect(filesB).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var templates = new Dictionary<string, TemplateCounts>(StringComparer.Ordinal);
            var unexplained = new List<Diff>();
            long totalDiffs = 0, volatileDiffs = 0, filesWithUnexplained = 0;

            foreach(var file in common) {
                var leavesA = Flatten(File.ReadAllText(Path.Combine(dirA, file)));
                var leavesB = Flatten(File.ReadAllText(Path.Combine(dirB, file)));
                var fileUnexplained = false;
                foreach(var path in leavesA.Keys.Union(leavesB.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)) {
                    var inA = leavesA.TryGetValue(path, out var before);
                    var inB = leavesB.TryGetValue(path, out var after);
                    if(inA && inB && before == after) continue;
                    var kind = inA && inB ? "changed" : inA ? "removed" : "added";
                    var diff = new Diff(file, path, kind, inA ? before : "", inB ? after : "");
                    totalDiffs++;
                    var template = Template(path);
                    if(!templates.TryGetValue(template, out var counts)) templates[template] = counts = new TemplateCounts();
                    if(IsVolatile(diff, patterns)) {
                        volatileDiffs++;
                        counts.Volatile++;
                    } else {
                        counts.Unexplained++;
                        counts.Example ??= diff;
                        unexplained.Add(diff);
                        fileUnexplained = true;
                    }
                }
                if(fileUnexplained) filesWithUnexplained++;
            }

            var csvPath = Path.Combine(outDir, "diff.csv");
            using(var writer = new StreamWriter(csvPath, false, new UTF8Encoding(false))) {
                writer.WriteLine("file,path,kind,before,after");
                foreach(var diff in unexplained)
                    writer.WriteLine(Csv.Line(diff.File, diff.Path, diff.Kind, diff.Before, diff.After));
            }

            var output = new StringBuilder();
            output.Append(Markdown.Heading(2, "Diff"));
            output.AppendLine(Markdown.Table(["metric", "value"], [
                ["files compared", Markdown.Num(common.Count)],
                ["files only in A", Markdown.Num(filesA.Except(filesB).Count())],
                ["files only in B", Markdown.Num(filesB.Except(filesA).Count())],
                ["files with unexplained diffs", Markdown.Num(filesWithUnexplained)],
                ["diffs total", Markdown.Num(totalDiffs)],
                ["volatile", Markdown.Num(volatileDiffs)],
                ["unexplained", Markdown.Num(unexplained.Count)],
                ["volatile patterns", Markdown.Num(patterns.Count)]
            ]));

            output.Append(Markdown.Heading(3, "Paths"));
            var ordered = templates.OrderByDescending(kv => kv.Value.Unexplained).ThenByDescending(kv => kv.Value.Volatile).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            output.AppendLine(Markdown.Table(["path", "volatile", "unexplained"],
                ordered.Take(MaxTemplateRows).Select(kv => (IReadOnlyList<string>)[kv.Key, Markdown.Num(kv.Value.Volatile), Markdown.Num(kv.Value.Unexplained)])));
            if(ordered.Count > MaxTemplateRows) output.AppendLine($"({ordered.Count - MaxTemplateRows} more path templates omitted)");

            output.Append(Markdown.Heading(3, $"Top {TopUnexplained} unexplained"));
            output.AppendLine(Markdown.Table(["path", "count", "example file", "kind", "before", "after"],
                ordered.Where(kv => kv.Value.Unexplained > 0).Take(TopUnexplained).Select(kv => (IReadOnlyList<string>)[
                    kv.Key, Markdown.Num(kv.Value.Unexplained), kv.Value.Example.File, kv.Value.Example.Kind,
                    Markdown.Clip(kv.Value.Example.Before), Markdown.Clip(kv.Value.Example.After)
                ])));
            output.AppendLine($"CSV: {csvPath}");
            Console.Write(output);
            return Task.FromResult(unexplained.Count > 0 ? 3 : 0);
        }

        private static List<Regex> BuildPatterns(string volatileFile) {
            var patterns = new List<Regex>();
            foreach(var name in VolatileNames)
                patterns.Add(new Regex("(^|/)" + Regex.Escape(name) + "(/|$)", RegexOptions.Compiled));
            foreach(var prefix in VolatilePrefixes)
                patterns.Add(new Regex("(^|/)" + Regex.Escape(prefix) + "[^/]*(/|$)", RegexOptions.Compiled));
            if(volatileFile is null) return patterns;
            if(!File.Exists(volatileFile)) throw new ArgumentException($"Volatile list not found: {volatileFile}");
            foreach(var line in File.ReadAllLines(volatileFile)) {
                var trimmed = line.Trim();
                if(trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                patterns.Add(new Regex(trimmed, RegexOptions.Compiled));
            }
            return patterns;
        }

        private static bool IsVolatile(Diff diff, List<Regex> patterns) {
            var path = diff.Path;
            if(diff.Kind != "changed" && Under(path, FarmsPath)) return true;
            if(diff.Kind == "added" && Under(path, ArchivedFarmsPath)) return true;
            if(diff.Kind == "removed" && diff.Before == "[]" && path is FarmsPath or ArchivedFarmsPath) return true;
            return patterns.Any(p => p.IsMatch(path));
        }

        private static bool Under(string path, string container) => path == container || path.StartsWith(container + "/", StringComparison.Ordinal);

        private static Dictionary<string, string> Flatten(string json) {
            var leaves = new Dictionary<string, string>(StringComparer.Ordinal);
            using var document = JsonDocument.Parse(json);
            Walk(document.RootElement, "", leaves);
            return leaves;
        }

        private static void Walk(JsonElement element, string path, Dictionary<string, string> leaves) {
            switch(element.ValueKind) {
                case JsonValueKind.Object:
                    var any = false;
                    foreach(var property in element.EnumerateObject()) {
                        any = true;
                        Walk(property.Value, path + "/" + Escape(property.Name), leaves);
                    }
                    if(!any) leaves[path] = "{}";
                    return;
                case JsonValueKind.Array:
                    if(element.GetArrayLength() == 0) {
                        leaves[path] = "[]";
                        return;
                    }
                    var keyed = path is FarmsPath or ArchivedFarmsPath;
                    var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                    var index = 0;
                    foreach(var item in element.EnumerateArray()) {
                        var segment = (keyed ? FarmKey(item, seen) : null) ?? index.ToString();
                        Walk(item, path + "/" + segment, leaves);
                        index++;
                    }
                    return;
                default:
                    leaves[path] = element.GetRawText();
                    return;
            }
        }

        private static string FarmKey(JsonElement item, Dictionary<string, int> seen) {
            if(item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("ContractId", out var id) || id.ValueKind != JsonValueKind.String) return null;
            var key = Escape(id.GetString() ?? "");
            if(key.Length == 0) return null;
            var occurrence = seen.GetValueOrDefault(key);
            seen[key] = occurrence + 1;
            return occurrence == 0 ? key : key + "#" + occurrence;
        }

        private static string Template(string path) {
            var segments = path.Split('/');
            for(var i = 1; i < segments.Length; i++) {
                var keyedFarm = i == 2 && (segments[1] == FarmsPath[1..] || segments[1] == ArchivedFarmsPath[1..]);
                if(keyedFarm || segments[i].All(char.IsDigit)) segments[i] = "*";
            }
            return string.Join("/", segments);
        }

        private static string Escape(string segment) => segment.Replace("~", "~0").Replace("/", "~1");
    }
}
