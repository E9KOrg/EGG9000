using EGG9000.Common.Database.Entities;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class SeasonalPeProgress {
        // True when the account still has PE to earn for its season grade. A grade with no PE goals
        // (maxPe == 0) counts as missing, preserving the legacy include behavior. Season goals are keyed
        // to the grade at season start, so StartingGrade wins over the live grade when progress exists.
        public static bool IsMissing(
            string accountId,
            Ei.Contract.Types.PlayerGrade liveGrade,
            SeasonInfo season,
            IEnumerable<UserSeasonProgress> progresses) {

            if(season is null) return false;

            var progress = (progresses ?? Enumerable.Empty<UserSeasonProgress>())
                .FirstOrDefault(p => p.EggIncId == accountId && p.SeasonId == season.Id);
            var seasonGrade = progress != null ? (Ei.Contract.Types.PlayerGrade)progress.StartingGrade : liveGrade;

            var maxPe = season.GetMaxPe(seasonGrade);
            if(maxPe == 0) return true;
            return season.GetPeEarned(seasonGrade, progress?.TotalCxp ?? 0) < maxPe;
        }

        // CS (season Cxp) at which this account earns all its season PE, resolved against the season-start
        // grade (same rule as IsMissing). 0 when there is no season or no PE goals for that grade.
        public static double CsGoalForPe(
            string accountId,
            Ei.Contract.Types.PlayerGrade liveGrade,
            SeasonInfo season,
            IEnumerable<UserSeasonProgress> progresses) {

            if(season is null) return 0;

            var progress = (progresses ?? Enumerable.Empty<UserSeasonProgress>())
                .FirstOrDefault(p => p.EggIncId == accountId && p.SeasonId == season.Id);
            var seasonGrade = progress != null ? (Ei.Contract.Types.PlayerGrade)progress.StartingGrade : liveGrade;
            return season.GetMaxPeCxp(seasonGrade);
        }
    }
}
