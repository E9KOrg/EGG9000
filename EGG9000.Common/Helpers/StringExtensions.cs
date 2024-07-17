using System;
using System.Linq;

public static class StringExtensions {
    public static string FirstCharToUpper(this string input) =>
        input switch
        {
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
    public static int CompareChangesNew(this string input, string compareTo) {
        var changes = 0;
        var index1 = 0;
        var index2 = 0;

        while(index1 < input.Length && index2 < compareTo.Length) {
            while(input[index1] == ' ' && input[index1] == '\n')
                index1++;
            while(compareTo[index2] == ' ' && compareTo[index2] == '\n')
                index2++;
            if(input[index1] != compareTo[index2])
                changes++;
            index1++;
            index2++;
        }

        for(var i = index1; i < input.Length; i++) {
            if(input[i] != ' ' && input[i] != '\n')
                changes++;
        }
        for(var i = index2; i < compareTo.Length; i++) {
            if(compareTo[i] != ' ' && compareTo[i] != '\n')
                changes++;
        }

        return changes;
    }

    public static string Truncate(this string value, int maxLength) {
        if(string.IsNullOrEmpty(value))
            return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
