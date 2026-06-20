using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EGG9000.Common.Helpers {
    public static class EditFaqValidation {
        public static List<string> ParseKeywords(string csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? []
                : [.. csv.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0)];

        public static string ValidateKeywords(IEnumerable<string> keywords) {
            var tooLong = keywords.FirstOrDefault(k => k.Length > FAQHelper.MAX_KEYWORD_LENGTH);
            return tooLong is null ? null : $"Keyword `{tooLong}` exceeds {FAQHelper.MAX_KEYWORD_LENGTH} characters.";
        }

        // Returns (normalized 6-hex without '#', error). Empty input clears the color (("", null)).
        public static (string Value, string Error) NormalizeColor(string raw) {
            raw = raw?.Trim() ?? "";
            if(raw.Length == 0) return ("", null);
            if(!Regex.IsMatch(raw, "^#?[0-9a-fA-F]{6}$")) return (null, $"`{raw}` is not a 6-digit hex color (e.g. `d43500`).");
            return (raw.Replace("#", "").ToLowerInvariant(), null);
        }
    }
}
