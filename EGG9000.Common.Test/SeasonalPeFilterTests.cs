using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Common.Test {
    // Seasonal PE option behavior (assign-if-missing / below-threshold) lives in ForceRuleTests.
    // These cover the pure progress arithmetic that feeds AccountFacts.MissingSeasonalPe.
    [TestClass]
    [TestCategory("Unit")]
    public class SeasonalPeProgressTests {
        // One grade-A goal at 1000 CXP = 1 PE.
        private static SeasonInfo MakeSeason(string id = "s1") {
            var goals = new Dictionary<int, List<SeasonPeGoal>> {
                [(int)G.GradeA] = [new SeasonPeGoal { Cxp = 1000, PeAmount = 1 }]
            };
            return new SeasonInfo {
                Id = id,
                Name = "Test Season",
                StartTime = DateTimeOffset.UnixEpoch,
                GoalsJson = JsonConvert.SerializeObject(goals)
            };
        }

        private static UserSeasonProgress MakeProgress(string eggIncId, string seasonId, double cxp, G grade) =>
            new() { EggIncId = eggIncId, SeasonId = seasonId, TotalCxp = cxp, StartingGrade = (int)grade };

        [TestMethod]
        public void NoPeEarned_IsMissing() {
            Assert.IsTrue(SeasonalPeProgress.IsMissing("u1", G.GradeA, MakeSeason(), new List<UserSeasonProgress>()));
        }

        [TestMethod]
        public void AllPeEarned_NotMissing() {
            var progress = new List<UserSeasonProgress> { MakeProgress("u1", "s1", 1000, G.GradeA) };
            Assert.IsFalse(SeasonalPeProgress.IsMissing("u1", G.GradeA, MakeSeason(), progress));
        }

        [TestMethod]
        public void NoGoalsForGrade_IsMissing() {
            // Season only has GradeA goals; a GradeB player has maxPe == 0 -> treated as missing.
            Assert.IsTrue(SeasonalPeProgress.IsMissing("u1", G.GradeB, MakeSeason(), new List<UserSeasonProgress>()));
        }

        [TestMethod]
        public void StartingGrade_WinsOverLiveGrade() {
            // Live grade B (no goals) but started the season at A (has goals) and earned none -> missing.
            var progress = new List<UserSeasonProgress> { MakeProgress("u1", "s1", 0, G.GradeA) };
            Assert.IsTrue(SeasonalPeProgress.IsMissing("u1", G.GradeB, MakeSeason(), progress));
        }

        [TestMethod]
        public void NullSeason_NotMissing() {
            Assert.IsFalse(SeasonalPeProgress.IsMissing("u1", G.GradeA, null, new List<UserSeasonProgress>()));
        }
    }
}
