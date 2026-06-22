using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class DualWriteSyncTests {
        // SyncLegacyKeysFromAssignment (blob -> old keys) must be the inverse of
        // AssignmentSettingsMigration.FromLegacyKeys (old keys -> blob) for every field that has an
        // old-key representation. ExcludeSeasonal is the one new field with no old-key home.
        private static void AssertRoundTrips(AssignmentSettings original) {
            var account = new EggIncAccount { Assignment = original };
            account.SyncLegacyKeysFromAssignment();
            var back = AssignmentSettingsMigration.FromLegacyKeys(account);

            Assert.AreEqual(original.Get(PermanentRewardKind.Colleggtible).Mode, back.Get(PermanentRewardKind.Colleggtible).Mode, "colleggtible");
            Assert.AreEqual(original.Get(PermanentRewardKind.SeasonalPe).Mode, back.Get(PermanentRewardKind.SeasonalPe).Mode, "seasonal mode");
            Assert.AreEqual(original.Get(PermanentRewardKind.SeasonalPe).CsFloor, back.Get(PermanentRewardKind.SeasonalPe).CsFloor, "seasonal floor");
            Assert.AreEqual(original.Redo.Mode, back.Redo.Mode, "redo mode");
            Assert.AreEqual(original.Redo.ScoreThreshold, back.Redo.ScoreThreshold, "redo threshold");
            Assert.AreEqual(original.TwoToThree, back.TwoToThree, "2to3");
            CollectionAssert.AreEqual(original.NewContractRewardFilter, back.NewContractRewardFilter, "new filter");
            CollectionAssert.AreEqual(original.LegacyRewardFilter, back.LegacyRewardFilter, "legacy filter");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RoundTrips_Defaults() {
            AssertRoundTrips(new AssignmentSettings());
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RoundTrips_SeasonalBelowThreshold_And_Filters() {
            var s = new AssignmentSettings {
                NewContractRewardFilter = new List<Ei.RewardType> { Ei.RewardType.Gold, Ei.RewardType.Artifact },
                LegacyRewardFilter = new List<Ei.RewardType> { Ei.RewardType.EggsOfProphecy },
                Redo = new RedoRule { Mode = RedoLeggacyOption.YesThreshold, ScoreThreshold = 15000 },
                TwoToThree = true
            };
            s.SetForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);
            s.SetForce(PermanentRewardKind.SeasonalPe, ForceMode.BelowThreshold, 5000);
            AssertRoundTrips(s);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RoundTrips_SeasonalAssignIfMissing() {
            var s = new AssignmentSettings();
            s.SetForce(PermanentRewardKind.SeasonalPe, ForceMode.AssignIfMissing);
            AssertRoundTrips(s);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Sync_WritesOldKeysOntoAccount() {
            var s = new AssignmentSettings {
                Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll },
                TwoToThree = true
            };
            s.SetForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);
            var account = new EggIncAccount { Assignment = s };
            account.SyncLegacyKeysFromAssignment();
            Assert.AreEqual(RedoLeggacyOption.YesAll, account.RedoLeggacySelection);
            Assert.IsTrue(account.DoTwoToThreeContracts);
            Assert.IsTrue(account.DoUnfinishedCollegtibles);
        }
    }
}
