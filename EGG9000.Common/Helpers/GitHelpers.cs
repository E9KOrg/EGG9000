using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class GitHelpers {
        public static ProcessResult ExecuteGitCommand(string command) {
            var processStartInfo = new ProcessStartInfo {
                FileName = "git",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();

            var output = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();

            process.WaitForExit();

            return new ProcessResult(process.ExitCode, output, error);
        }

        public class ProcessResult {
            public int ExitCode { get; }
            public string Output { get; }
            public string Error { get; }

            public ProcessResult(int exitCode, string output, string error) {
                ExitCode = exitCode;
                Output = output;
                Error = error;
            }
        }

        public static string ConvertToDiscordTimestamp(string gitTimestamp) {
            // Parse the time value and unit from the input text
            var value = int.Parse(gitTimestamp.Split(' ')[0]);
            var unit = gitTimestamp.Split(' ')[1].ToLower();

            // Calculate the corresponding DateTime based on the time unit
            var timestamp = unit switch {
                "second" or "seconds" => DateTime.Now.AddSeconds(-value),
                "minute" or "minutes" => DateTime.Now.AddMinutes(-value),
                "hour" or "hours" => DateTime.Now.AddHours(-value),
                "day" or "days" => DateTime.Now.AddDays(-value),
                "week" or "weeks" => DateTime.Now.AddDays(-7 * value),
                "month" or "months" => DateTime.Now.AddMonths(-value),
                "year" or "years" => DateTime.Now.AddYears(-value),
                _ => throw new ArgumentException("Invalid time unit provided."),
            };

            // Convert DateTime to Unix timestamp (seconds)
            var unixTimestamp = (long)timestamp.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

            // Format the timestamp in the Discord timestamp format
            return $"<t:{unixTimestamp}:R>";
        }
    }
}
