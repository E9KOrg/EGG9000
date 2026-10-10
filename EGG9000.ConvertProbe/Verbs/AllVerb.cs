using Npgsql;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.ConvertProbe.Verbs {
    public static class AllVerb {
        private sealed record Step(string Name, string File, Func<ProbeOptions, Task<int>> Run, int? Limit);

        private static readonly Step[] Steps = [
            new("formats", "formats.md", FormatsVerb.RunAsync, null),
            new("coverage", "coverage.md", CoverageVerb.RunAsync, null),
            new("goalsets", "goalsets.md", GoalSetsVerb.RunAsync, null),
            new("bench", "bench-console.md", BenchVerb.RunAsync, 500),
            new("query-demo", "query-demo.md", QueryDemoVerb.RunAsync, null),
            new("dump", "dump.md", DumpVerb.RunAsync, null)
        ];

        public static async Task<int> RunAsync(ProbeOptions options) {
            var root = options.EnsureOutDir();
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssZ");
            var runDir = Path.Combine(root, stamp);
            Directory.CreateDirectory(runDir);
            options.OutDir = runDir;
            options.Csv = Path.Combine(root, "formats-timeline.csv");
            var requestedLimit = options.Limit;

            var summary = new StringBuilder();
            summary.Append(Markdown.Heading(1, $"convertprobe all {stamp}"));
            summary.AppendLine($"- database: {new NpgsqlConnectionStringBuilder(options.Conn).Database}");
            summary.AppendLine($"- output: {runDir}");
            summary.AppendLine($"- started: {DateTime.UtcNow:O}");
            summary.AppendLine();
            var rows = new List<IReadOnlyList<string>>();
            var worst = 0;
            var realOut = Console.Out;

            foreach(var step in Steps) {
                Console.Error.WriteLine($"all: {step.Name} starting");
                options.Limit = step.Limit ?? requestedLimit;
                var timer = Stopwatch.StartNew();
                var capture = new StringWriter();
                var code = 0;
                string failure = null;
                Console.SetOut(capture);
                try {
                    code = await step.Run(options);
                } catch(Exception e) {
                    code = 1;
                    failure = e.GetType().Name + ": " + e.Message;
                } finally {
                    Console.SetOut(realOut);
                }
                timer.Stop();
                var text = capture.ToString();
                if(failure is not null)
                    text += Environment.NewLine + "FAILED: " + failure + Environment.NewLine;
                await File.WriteAllTextAsync(Path.Combine(runDir, step.File), text);
                Console.Write(text);
                Console.Error.WriteLine($"all: {step.Name} exit {code} in {timer.Elapsed:mm\\:ss}");
                rows.Add([step.Name, step.File, code.ToString(), timer.Elapsed.ToString("hh\\:mm\\:ss"), failure ?? ""]);
                worst = Math.Max(worst, code);
            }

            summary.AppendLine(Markdown.Table(["step", "file", "exit", "elapsed", "failure"], rows));
            summary.AppendLine($"- finished: {DateTime.UtcNow:O}");
            await File.WriteAllTextAsync(Path.Combine(runDir, "summary.md"), summary.ToString());
            Console.Write(summary);
            return worst;
        }
    }
}
