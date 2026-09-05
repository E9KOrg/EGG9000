using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EGG9000.ConvertProbe {
    public static class Markdown {
        public static string Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows) {
            var builder = new StringBuilder();
            builder.Append("| ").Append(string.Join(" | ", headers.Select(Escape))).AppendLine(" |");
            builder.Append('|').Append(string.Concat(headers.Select(_ => "---|"))).AppendLine();
            foreach(var row in rows)
                builder.Append("| ").Append(string.Join(" | ", row.Select(Escape))).AppendLine(" |");
            return builder.ToString();
        }

        public static string Heading(int level, string text) => new string('#', level) + " " + text + Environment.NewLine + Environment.NewLine;

        public static string Bytes(long bytes) {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            var unit = 0;
            while(Math.Abs(value) >= 1024 && unit < units.Length - 1) {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? $"{bytes} B" : $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {units[unit]}";
        }

        public static string Num(double value, int decimals = 2) => value.ToString("F" + decimals, CultureInfo.InvariantCulture);

        public static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Percent(double part, double whole) => whole == 0 ? "n/a" : (100.0 * part / whole).ToString("0.00", CultureInfo.InvariantCulture) + "%";

        public static string Clip(string value, int max = 60) {
            value ??= "";
            return value.Length <= max ? value : value[..max] + "...";
        }

        private static string Escape(string cell) => (cell ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}
