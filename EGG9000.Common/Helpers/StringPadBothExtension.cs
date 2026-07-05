namespace EGG9000.Common.Helpers {
    public static class StringExtensions {
        public static string PadBoth(this string str, int length) {
            var spaces = length - str.Length;
            var padLeft = spaces / 2 + str.Length;
            return str.PadLeft(padLeft).PadRight(length);
        }
    }
}