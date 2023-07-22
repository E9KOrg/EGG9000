using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EGG9000.Common.Contracts {
    public static class PlayerGradeDetails {
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
                _ => "None",
            };
        }

        public static string GetImage(uint league) {
            return GetImage((Ei.Contract.Types.PlayerGrade)league);
        }
            public static string GetImage(Ei.Contract.Types.PlayerGrade grade) {
                var emoji = GetEmoji(grade);

            var rgx = new Regex(@":(\d+)>");
            var id = rgx.Match(emoji).Groups[1];
            return $"https://cdn.discordapp.com/emojis/{id}.png?v=1";
        }
        public static string GetNameFromLeague(uint league) {
            return GetNameFromLeague((int)league);
        }

        public static String GetAutoCompleteSuggestion(Ei.Contract.Types.PlayerGrade grade) {
            return GetText(grade) + " - " + GetNameFromLeague((int)grade);
        }

        public static string GetNameFromLeague(int league) {
            switch(league) {
                case 1:
                    return "C";
                case 2:
                    return "B";
                case 3:
                    return "A";
                case 4:
                    return "AA";
                case 5:
                    return "AAA";
                default:
                    return "Not Set";
            }
        }
    }
}
