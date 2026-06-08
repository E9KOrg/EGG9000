using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EGG9000.Bot.Helpers {
    public class FixedWidthTable {
        public static List<string> GetTableListFormatted(List<List<FixedWidthCell>> contents) {
            var table = GetTable(contents);
            var rows = table.Split("\n");
            return [..rows.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => $"`{x}`")];
        }
        public static string GetTable(List<List<FixedWidthCell>> contents) {
            var sb = new StringBuilder();

            //Find max column widths
            var columnWidths = new Dictionary<int, int>();
            foreach((var row, int rowIndex) in contents.Select((item, i) => (item, i))) {
                if(row == null)
                    continue;
                foreach((var cell, int columnIndex) in row.Select((item, i) => (item, i))) {
                    var content = cell.Content?.Replace("**", "");
                    if(columnWidths.ContainsKey(columnIndex) && columnWidths[columnIndex] < (cell.OverrideWidth ?? (content ?? "").Length)) {
                        columnWidths[columnIndex] = cell.OverrideWidth ?? content.Length;
                    } else if(!columnWidths.ContainsKey(columnIndex)) {
                        columnWidths.Add(columnIndex, cell.OverrideWidth ?? content.Length);
                    }
                }
            }

            foreach((var row, int rowIndex) in contents.Select((item, i) => (item, i))) {
                if(row == null)
                    continue;
                foreach((var cell, int columnIndex) in row.Select((item, i) => (item, i))) {
                    var padding = columnWidths[columnIndex];
                    if(cell.ReducePadding)
                        padding--;
                    switch(cell.Align) {
                        case CellAlignment.Left:
                            sb.Append((cell.Content ?? "").PadRight(padding));
                            break;
                        case CellAlignment.Right:
                            sb.Append((cell.Content ?? "").PadLeft(padding));
                            break;
                        case CellAlignment.Center:
                            sb.Append((cell.Content ?? "").PadBoth(padding));
                            break;
                    }
                    sb.Append(" ");
                }
                sb.Append("\n");
            }

            return sb.ToString();
        }

        public class FixedWidthCell {
            public string Content { get; set; }
            public CellAlignment Align { get; set; }
            public bool ReducePadding { get; set; }
            public int? OverrideWidth { get; set; }

            public FixedWidthCell() { }
            public FixedWidthCell(string c, CellAlignment a = CellAlignment.Left, bool reducePadding = false, int? overrideWidth = null) {
                Content = c;
                Align = a;
                ReducePadding = reducePadding;
                OverrideWidth = overrideWidth;
            }
        }

        public enum CellAlignment {
            Left,
            Center,
            Right
        }
    }
}
