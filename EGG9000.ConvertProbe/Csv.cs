using System.Linq;

namespace EGG9000.ConvertProbe {
    public static class Csv {
        public static string Field(string value) {
            value ??= "";
            return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public static string Line(params string[] fields) => string.Join(",", fields.Select(Field));
    }
}
