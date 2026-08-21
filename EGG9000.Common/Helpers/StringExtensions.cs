using System;
using System.Linq;
using System.Text;

public static class StringExtensions {
    // "HighestEB" -> "Highest EB". Space goes before every uppercase letter that follows a
    // non-uppercase one, so runs of capitals (acronyms like EB) are left alone.
    public static string SplitPascalCase(this string value) {
        if(string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder(value.Length + 8);
        for(var i = 0; i < value.Length; i++) {
            if(i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
                sb.Append(' ');
            sb.Append(value[i]);
        }
        return sb.ToString();
    }

    public static string FirstCharToUpper(this string input) =>
        input switch {
            null => "",
            "" => "",
            _ => input.First().ToString().ToUpper() + input.Substring(1)
        };

    public static int CompareChanges(this string input, string compareTo) {
        var msg1 = input.Replace(" ", "").Trim('\n');
        var msg2 = compareTo.Replace(" ", "").Trim('\n');
        var changes = 0;
        for(var j = 0; j < Math.Max(msg1.Length, msg2.Length); j++) {
            var char1 = j < msg1.Length ? msg1[j] : 0;
            var char2 = j < msg2.Length ? msg2[j] : 0;
            if(char1 != char2) {
                changes++;
            }
        }
        if(changes < 6 && changes > 0) {

        }

        return changes;
    }

    public static string Truncate(this string value, int maxLength) {
        if(string.IsNullOrEmpty(value))
            return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
