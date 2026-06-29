using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class DualWriteSyncTests {
        [TestMethod]
        [TestCategory("Unit")]
        public void Sync_MapsFilterToBothLists() {
            var s = new AssignmentSettings { RewardFilter = new() { Ei.RewardType.Gold, Ei.RewardType.Artifact } };
            var acc = new EggIncAccount { Assignment = s };
            acc.SyncLegacyKeysFromAssignment();

            CollectionAssert.AreEqual(new List<Ei.RewardType> { Ei.RewardType.Gold, Ei.RewardType.Artifact }, acc.AutoRegisterRewards);
            CollectionAssert.AreEqual(new List<Ei.RewardType> { Ei.RewardType.Gold, Ei.RewardType.Artifact }, acc.LeggacyAutoRegisterRewards);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Sync_UntilCsGoal_MapsToAssignIfBelowThreshold() {
            var s = new AssignmentSettings {
                RewardFilter = new() { Ei.RewardType.Gold },
                Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilCsGoal, CsGoal = 9000 }
            };
            var acc = new EggIncAccount { Assignment = s };
            acc.SyncLegacyKeysFromAssignment();

            Assert.AreEqual(SeasonalPeOption.AssignIfBelowThreshold, acc.SeasonalPeOption);
            Assert.AreEqual(9000d, acc.SeasonalPeThreshold);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Sync_AlwaysAssignAndUntilPe_MapToAlwaysAssignIfMissing() {
            var always = new EggIncAccount { Assignment = new AssignmentSettings { Seasonal = new SeasonalRule { Mode = SeasonalMode.AlwaysAssign } } };
            always.SyncLegacyKeysFromAssignment();
            Assert.AreEqual(SeasonalPeOption.AlwaysAssignIfMissing, always.SeasonalPeOption);

            var untilPe = new EggIncAccount { Assignment = new AssignmentSettings { Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilPeEarned } } };
            untilPe.SyncLegacyKeysFromAssignment();
            Assert.AreEqual(SeasonalPeOption.AlwaysAssignIfMissing, untilPe.SeasonalPeOption);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Sync_RedoTwoToThreeColleggtible_PassThrough() {
            var s = new AssignmentSettings {
                Redo = new RedoRule { Mode = RedoLeggacyOption.YesThreshold, ScoreThreshold = 33000 },
                TwoToThree = true
            };
            s.SetForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);
            var acc = new EggIncAccount { Assignment = s };
            acc.SyncLegacyKeysFromAssignment();

            Assert.AreEqual(RedoLeggacyOption.YesThreshold, acc.RedoLeggacySelection);
            Assert.AreEqual(33000, acc.RedoScoreThreshold);
            Assert.IsTrue(acc.DoTwoToThreeContracts);
            Assert.IsTrue(acc.DoUnfinishedCollegtibles);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Sync_NullAssignment_NoOp() {
            var acc = new EggIncAccount { Assignment = null, AutoRegisterRewards = new() { Ei.RewardType.Cash } };
            acc.SyncLegacyKeysFromAssignment();
            // Untouched when there is nothing to sync.
            CollectionAssert.AreEqual(new List<Ei.RewardType> { Ei.RewardType.Cash }, acc.AutoRegisterRewards);
        }
    }
}
