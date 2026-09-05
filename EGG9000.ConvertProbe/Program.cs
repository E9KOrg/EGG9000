using EGG9000.ConvertProbe.Verbs;
using Npgsql;
using System;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe {
    public static class Program {
        public static async Task<int> Main(string[] args) {
            var options = ProbeOptions.Parse(args, out var error);
            if(error is not null) {
                Console.Error.WriteLine(error);
                PrintHelp();
                return 1;
            }
            if(options.Help || options.Verb is null) {
                PrintHelp();
                return options.Help ? 0 : 1;
            }
            if(options.Verb != "diff" && string.IsNullOrWhiteSpace(options.Conn)) {
                Console.Error.WriteLine("--conn is required.");
                return 1;
            }
            try {
                return options.Verb switch {
                    "formats" => await FormatsVerb.RunAsync(options),
                    "coverage" => await CoverageVerb.RunAsync(options),
                    "inspect" => await InspectVerb.RunAsync(options),
                    "goalsets" => await GoalSetsVerb.RunAsync(options),
                    "dump" => await DumpVerb.RunAsync(options),
                    "diff" => await DiffVerb.RunAsync(options),
                    "bench" => await BenchVerb.RunAsync(options),
                    "query-demo" => await QueryDemoVerb.RunAsync(options),
                    "all" => await AllVerb.RunAsync(options),
                    _ => Unknown(options.Verb)
                };
            } catch(NpgsqlException e) {
                Console.Error.WriteLine("Database error: " + e.Message);
                return 1;
            } catch(ArgumentException e) {
                Console.Error.WriteLine("Usage error: " + e.Message);
                return 1;
            }
        }

        private static int Unknown(string verb) {
            Console.Error.WriteLine($"Unknown verb: {verb}");
            PrintHelp();
            return 1;
        }

        private static void PrintHelp() {
            Console.WriteLine("EGG9000.ConvertProbe: read-only storage-format conversion probe.");
            Console.WriteLine();
            Console.WriteLine("Usage: convertprobe <verb> --conn \"<npgsql connection string>\" [--out <dir>] [--limit N]");
            Console.WriteLine();
            Console.WriteLine("Verbs:");
            Console.WriteLine("  formats [--csv <file>]        first-byte histograms, _response presence, relation sizes, AutomationLogs 24h");
            Console.WriteLine("  coverage                      decode every accounts blob, report parent-blob presence, write coverage.csv");
            Console.WriteLine("  inspect [--all]               schema-aware raw msgpack walk of accounts blobs that fail to decode (--all: every blob), report per-slot overflow/type mismatch, write inspect.csv");
            Console.WriteLine("  goalsets                      quantify archived/active farms whose Completed flips under GoalSets[League] vs GoalSets[0], write goalsets.csv");
            Console.WriteLine("  dump                          canonical JSON per CustomBackup into <out>/dump");
            Console.WriteLine("  diff <dirA> <dirB> [--volatile <file>]  compare two dumps, write diff.csv");
            Console.WriteLine("  bench                         decode/encode timings and sizes (legacy, envelope, master-equivalent) plus LZ4 block layout, write bench.md");
            Console.WriteLine("  query-demo                    LINQ over a parsed EiBackup field no legacy projection stored");
            Console.WriteLine("  all                           formats, coverage, goalsets, bench, query-demo, dump into <out>/<utc stamp>/ plus summary.md; formats also appended to <out>/formats-timeline.csv");
            Console.WriteLine();
            Console.WriteLine($"Defaults: --out {ProbeOptions.DefaultOutDir}, bench --limit 200.");
            Console.WriteLine("Exit codes: 0 ok, 1 usage or connection error, 2 coverage decode failure, 3 unexplained diff.");
        }
    }
}
