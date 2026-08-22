
using EGG9000.Common.Database.Entities;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        public static int GetDigit(int number, int digit) {
            for(var i = 0; i < digit - 1; i++)
                number /= 10;
            return number % 10;
        }

        private static int LevenshteinRatio(string a, string b) {
            var maxLen = Math.Max(a.Length, b.Length);
            if(maxLen == 0) return 100;
            return (int)Math.Round((1.0 - (double)LevenshteinDistance(a, b) / maxLen) * 100);
        }

        private static int LevenshteinDistance(string a, string b) {
            var d = new int[a.Length + 1, b.Length + 1];
            for(var i = 0; i <= a.Length; i++) d[i, 0] = i;
            for(var j = 0; j <= b.Length; j++) d[0, j] = j;
            for(var i = 1; i <= a.Length; i++) {
                for(var j = 1; j <= b.Length; j++) {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[a.Length, b.Length];
        }

        public bool CheckForCreator(Coop coop, CoopDetails coopDetails) {
            if(string.IsNullOrEmpty(coop.CreatorID)) {
                var creator = coopDetails.CoopParticipants.FirstOrDefault(x => x.Backup is not null && x.Backup.Farms.Any(y => y.Creator && y.CoopId.Equals(coop.Name, StringComparison.CurrentCultureIgnoreCase) && y.ContractId == coop.ContractID));
                if(creator != null) {
                    coop.CreatorID = creator.EggIncId;
                    return true;
                }
            }
            return false;
        }

        [GeneratedRegex(@"\p{Cs}")]
        private static partial Regex MyRegex();
    }
}
