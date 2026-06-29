using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class MigrationTests {
        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_LegacyWinsElseMain_NoPe() {
            var a = new EggIncAccount {
                AutoRegisterRewards = new() { Ei.RewardType.EggsOfProphecy, Ei.RewardType.Gold },
                LeggacyAutoRegisterRewards = new() { Ei.RewardType.Artifact, Ei.RewardType.EggsOfProphecy }
            };
            var s = AssignmentSettingsMigration.FromLegacyKeys(a);
            CollectionAssert.AreEquivalent(new List<Ei.RewardType> { Ei.RewardType.Artifact }, s.RewardFilter);

            var b = new EggIncAccount { AutoRegisterRewards = new() { Ei.RewardType.Gold }, LeggacyAutoRegisterRewards = new() };
            CollectionAssert.AreEquivalent(new List<Ei.RewardType> { Ei.RewardType.Gold }, AssignmentSettingsMigration.FromLegacyKeys(b).RewardFilter);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_StripsUnknownReward() {
            var a = new EggIncAccount {
                AutoRegisterRewards = new() { Ei.RewardType.UnknownReward, Ei.RewardType.Gold },
                LeggacyAutoRegisterRewards = new()
            };
            CollectionAssert.AreEquivalent(new List<Ei.RewardType> { Ei.RewardType.Gold }, AssignmentSettingsMigration.FromLegacyKeys(a).RewardFilter);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Seasonal_Maps() {
            SeasonalRule M(SeasonalPeOption o, double thr = 0) =>
                AssignmentSettingsMigration.FromLegacyKeys(new EggIncAccount { SeasonalPeOption = o, SeasonalPeThreshold = thr }).Seasonal;

            Assert.AreEqual(SeasonalMode.AlwaysAssign, M(SeasonalPeOption.NotSet).Mode);
            Assert.AreEqual(SeasonalMode.AlwaysAssign, M(SeasonalPeOption.AlwaysAssignIfMissing).Mode);
            Assert.AreEqual(SeasonalMode.AlwaysAssign, M(SeasonalPeOption.DontAssign).Mode);

            var thr = M(SeasonalPeOption.AssignIfBelowThreshold, 7000);
            Assert.AreEqual(SeasonalMode.UntilCsGoal, thr.Mode);
            Assert.AreEqual(7000d, thr.CsGoal);
            Assert.IsFalse(thr.RewardFilterAfter);
            Assert.IsFalse(M(SeasonalPeOption.AlwaysAssignIfMissing).RewardFilterAfter);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_PassesThrough() {
            var on = AssignmentSettingsMigration.FromLegacyKeys(new EggIncAccount { DoUnfinishedCollegtibles = true });
            Assert.AreEqual(ForceMode.AssignIfMissing, on.Get(PermanentRewardKind.Colleggtible).Mode);

            var off = AssignmentSettingsMigration.FromLegacyKeys(new EggIncAccount { DoUnfinishedCollegtibles = false });
            Assert.AreEqual(ForceMode.NotSet, off.Get(PermanentRewardKind.Colleggtible).Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RedoAndTwoToThree_PassThrough() {
            var a = new EggIncAccount {
                RedoLeggacySelection = RedoLeggacyOption.YesThreshold,
                RedoScoreThreshold = 45000,
                DoTwoToThreeContracts = true
            };
            var s = AssignmentSettingsMigration.FromLegacyKeys(a);
            Assert.AreEqual(RedoLeggacyOption.YesThreshold, s.Redo.Mode);
            Assert.AreEqual(45000, s.Redo.ScoreThreshold);
            Assert.IsFalse(s.Redo.ExcludeSeasonal);
            Assert.IsTrue(s.TwoToThree);
        }
    }
}
