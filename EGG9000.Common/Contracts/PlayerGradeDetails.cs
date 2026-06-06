using System.Text.RegularExpressions;

namespace EGG9000.Common.Contracts {
    public static partial class PlayerGradeDetails {
        public static string GetEmoji(uint league) {
            return GetEmoji((Ei.Contract.Types.PlayerGrade)league);
        }
        public static string GetEmoji(Ei.Contract.Types.PlayerGrade grade) {
            return grade switch {
                Ei.Contract.Types.PlayerGrade.GradeAaa => "<:grade_aaa:1102985471862247426>",
                Ei.Contract.Types.PlayerGrade.GradeAa => "<:grade_aa:1102985434562297989>",
                Ei.Contract.Types.PlayerGrade.GradeA => "<:grade_a:1102985380845867068>",
                Ei.Contract.Types.PlayerGrade.GradeB => "<:grade_b:1102985335920668742>",
                Ei.Contract.Types.PlayerGrade.GradeC => "<:grade_c:1102984452935794799>",
                _ => "None",
            };
        }

        public static string GetText(Ei.Contract.Types.PlayerGrade grade) {
            return grade switch {
                Ei.Contract.Types.PlayerGrade.GradeAaa => "AAA",
                Ei.Contract.Types.PlayerGrade.GradeAa => "AA",
                Ei.Contract.Types.PlayerGrade.GradeA => "A",
                Ei.Contract.Types.PlayerGrade.GradeB => "B",
                Ei.Contract.Types.PlayerGrade.GradeC => "C",
                _ => "Not Set",
            };
        }

        public static string GetImage(uint league) =>
            GetImage((Ei.Contract.Types.PlayerGrade)league);

        public static string GetImage(Ei.Contract.Types.PlayerGrade grade) =>
            $"https://cdn.discordapp.com/emojis/{EmojiRegex().Match(GetEmoji(grade)).Groups[1]}.png?v=1";

        public static string GetNameFromLeague(uint league) =>
            GetNameFromLeague((int)league);

        public static string GetNameFromLeague(int league) =>
            GetText((Ei.Contract.Types.PlayerGrade)league);

        public static Ei.Contract.Types.PlayerGrade GetGradeFromLeague(uint league) =>
            (Ei.Contract.Types.PlayerGrade)league;

        [GeneratedRegex(@":(\d+)>")]
        private static partial Regex EmojiRegex();
    }
}
