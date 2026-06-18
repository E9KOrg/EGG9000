using EGG9000.Common.Contracts;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Common.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class SeasonalPeFilterTests {
        // Helper: build a minimal SeasonInfo with one grade-A goal at 1000 CXP = 1 PE
        private static SeasonInfo MakeSeason(string id = "s1") {
            var goals = new Dictionary<int, List<SeasonPeGoal>> {
                [(int)G.GradeA] = [new SeasonPeGoal { Cxp = 1000, PeAmount = 1 }]
            };
            return new SeasonInfo {
                Id = id,
                Name = "Test Season",
                StartTime = DateTimeOffset.UtcNow.AddDays(-7),
                GoalsJson = JsonConvert.SerializeObject(goals)
            };
        }

        private static UserSeasonProgress MakeProgress(string eggIncId, string seasonId, double cxp, G grade) =>
            new() { EggIncId = eggIncId, SeasonId = seasonId, TotalCxp = cxp, StartingGrade = (int)grade };

        [TestMethod]
        public void ShouldIncludeForSeasonalPe_NotSet_AlwaysInclude() {
            var season = MakeSeason();
            var account = new EggIncAccount { Id = "u1", SeasonalPeOption = SeasonalPeOption.NotSet };
            var progress = new List<UserSeasonProgress>();
            Assert.IsTrue(OrganizeCoops.ShouldIncludeForSeasonalPe(account, G.GradeA, season, progress, null));
        }

        [TestMethod]
        public void ShouldIncludeForSeasonalPe_DontAssign_AlwaysExclude() {
            var season = MakeSeason();
            var account = new EggIncAccount { Id = "u1", SeasonalPeOption = SeasonalPeOption.DontAssign };
            var progress = new List<UserSeasonProgress>();
            Assert.IsFalse(OrganizeCoops.ShouldIncludeForSeasonalPe(account, G.GradeA, season, progress, null));
        }

        [TestMethod]
        public void ShouldIncludeForSeasonalPe_AlwaysAssign_NoPeEarned_Include() {
            var season = MakeSeason();
            var account = new EggIncAccount { Id = "u1", SeasonalPeOption = SeasonalPeOption.AlwaysAssignIfMissing };
            var progress = new List<UserSeasonProgress>();
            Assert.IsTrue(OrganizeCoops.ShouldIncludeForSeasonalPe(account, G.GradeA, season, progress, null));
        }

        [TestMethod]
        public void ShouldIncludeForSeasonalPe_AlwaysAssign_AllPeEarned_Exclude() {
            var season = MakeSeason();
            var account = new EggIncAccount { Id = "u1", SeasonalPeOption = SeasonalPeOption.AlwaysAssignIfMissing };
            var progress = new List<UserSeasonProgress> {
                MakeProgress("u1", "s1", 1000, G.GradeA)
            };
            Assert.IsFalse(OrganizeCoops.ShouldIncludeForSeasonalPe(account, G.GradeA, season, progress, null));
        }

        [TestMethod]
        public void ShouldIncludeForSeasonalPe_BelowThreshold_Include() {
            // Season total is 250k but contract score was 15k < threshold 20k → include
            var season = MakeSeason();
            var account = new EggIncAccount { Id = "u1", SeasonalPeOption = SeasonalPeOption.AssignIfBelowThreshold, SeasonalPeThreshold = 20000 };
            var progress = new List<UserSeasonProgress> {
                MakeProgress("u1", "s1", 250000, G.GradeA) // high season total, irrelevant for this branch
            };
            Assert.IsTrue(OrganizeCoops.ShouldIncludeForSeasonalPe(account, G.GradeA, season, progress, contractScore: 15000));
        }

        [TestMethod]
        public void ShouldIncludeForSeasonalPe_AboveThreshold_Exclude() {
            // Contract score 25k >= threshold 20k → exclude
            var season = MakeSeason();
            var account = new EggIncAccount { Id = "u1", SeasonalPeOption = SeasonalPeOption.AssignIfBelowThreshold, SeasonalPeThreshold = 20000 };
            var progress = new List<UserSeasonProgress> {
                MakeProgress("u1", "s1", 250000, G.GradeA)
            };
            Assert.IsFalse(OrganizeCoops.ShouldIncludeForSeasonalPe(account, G.GradeA, season, progress, contractScore: 25000));
        }

        [TestMethod]
        public void ShouldIncludeForSeasonalPe_BelowThreshold_NullScore_Include() {
            // Never played contract before (null score) → 0 < threshold → include
            var season = MakeSeason();
            var account = new EggIncAccount { Id = "u1", SeasonalPeOption = SeasonalPeOption.AssignIfBelowThreshold, SeasonalPeThreshold = 20000 };
            var progress = new List<UserSeasonProgress>();
            Assert.IsTrue(OrganizeCoops.ShouldIncludeForSeasonalPe(account, G.GradeA, season, progress, contractScore: null));
        }

        [TestMethod]
        public void ShouldIncludeForSeasonalPe_NoGoalsForGrade_Include() {
            // Grade with no PE goals in season → not a seasonal grade → include regardless
            var season = MakeSeason(); // only has GradeA goals
            var account = new EggIncAccount { Id = "u1", SeasonalPeOption = SeasonalPeOption.AlwaysAssignIfMissing };
            var progress = new List<UserSeasonProgress>();
            Assert.IsTrue(OrganizeCoops.ShouldIncludeForSeasonalPe(account, G.GradeB, season, progress, null));
        }
    }
}
