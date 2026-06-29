using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class SeasonalPeProgressTests {
        private const string SeasonId = "season-1";
        private const string AccountId = "acct-1";

        // Builds a SeasonInfo whose GoalsJson is the JSON of Dictionary<int, List<SeasonPeGoal>> keyed by
        // (int)PlayerGrade, matching the prod serialization shape.
        private static SeasonInfo Season(IDictionary<int, List<SeasonPeGoal>> goals) =>
            new() { Id = SeasonId, GoalsJson = JsonConvert.SerializeObject(goals) };

        private static SeasonInfo GradeCSeason(params (double cxp, int pe)[] goals) {
            var list = new List<SeasonPeGoal>();
            foreach(var (cxp, pe) in goals)
                list.Add(new SeasonPeGoal { Cxp = cxp, PeAmount = pe });
            return Season(new Dictionary<int, List<SeasonPeGoal>> { [(int)G.GradeC] = list });
        }

        private static List<UserSeasonProgress> Progress(double totalCxp, int startingGrade) =>
            new() { new UserSeasonProgress { EggIncId = AccountId, SeasonId = SeasonId, TotalCxp = totalCxp, StartingGrade = startingGrade } };

        [TestMethod]
        [TestCategory("Unit")]
        public void NullSeason_NotMissing() {
            Assert.IsFalse(SeasonalPeProgress.IsMissing(AccountId, G.GradeC, null, null));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NoGoalsForGrade_CountsAsMissing() {
            // Season has goals only for GradeB; the account is GradeC -> maxPe 0 -> missing.
            var season = Season(new Dictionary<int, List<SeasonPeGoal>> {
                [(int)G.GradeB] = new() { new SeasonPeGoal { Cxp = 100, PeAmount = 5 } }
            });
            Assert.IsTrue(SeasonalPeProgress.IsMissing(AccountId, G.GradeC, season, null));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NoPeEarned_IsMissing() {
            var season = GradeCSeason((cxp: 1000, pe: 5));
            // No progress row, TotalCxp treated as 0 -> earned 0 < 5 -> missing.
            Assert.IsTrue(SeasonalPeProgress.IsMissing(AccountId, G.GradeC, season, null));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AllPeEarned_NotMissing() {
            var season = GradeCSeason((cxp: 1000, pe: 5));
            // Progress at/above the goal cxp -> earned 5 == maxPe 5 -> not missing.
            Assert.IsFalse(SeasonalPeProgress.IsMissing(AccountId, G.GradeC, season, Progress(1000, (int)G.GradeC)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PartialPeEarned_StillMissing() {
            var season = GradeCSeason((cxp: 1000, pe: 5), (cxp: 2000, pe: 5));
            // Reaches the first goal only -> earned 5 < maxPe 10 -> missing.
            Assert.IsTrue(SeasonalPeProgress.IsMissing(AccountId, G.GradeC, season, Progress(1500, (int)G.GradeC)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void StartingGrade_WinsOverLiveGrade() {
            // Goals exist only for GradeC. Live grade is GradeB (no goals -> would be missing),
            // but StartingGrade is GradeC with all PE earned -> not missing.
            var season = GradeCSeason((cxp: 1000, pe: 5));
            Assert.IsFalse(SeasonalPeProgress.IsMissing(AccountId, G.GradeB, season, Progress(1000, (int)G.GradeC)));
        }
    }
}
