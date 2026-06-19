using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class MigrationTests {
        [TestMethod]
        [TestCategory("Unit")]
        public void Pe_DroppedFromNew_KeptInLegacy() {
            var a = new EggIncAccount {
                AutoRegisterRewards = new List<Ei.RewardType> { Ei.RewardType.EggsOfProphecy, Ei.RewardType.Gold },
                LeggacyAutoRegisterRewards = new List<Ei.RewardType> { Ei.RewardType.EggsOfProphecy }
            };
            var s = AssignmentSettingsMigration.FromLegacyKeys(a);
            CollectionAssert.DoesNotContain(s.NewContractRewardFilter, Ei.RewardType.EggsOfProphecy);
            CollectionAssert.Contains(s.NewContractRewardFilter, Ei.RewardType.Gold);
            CollectionAssert.Contains(s.LegacyRewardFilter, Ei.RewardType.EggsOfProphecy);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AnyRewardSentinel_StrippedFromBothFilters() {
            var a = new EggIncAccount {
                AutoRegisterRewards = new List<Ei.RewardType> { Ei.RewardType.UnknownReward, Ei.RewardType.Gold },
                LeggacyAutoRegisterRewards = new List<Ei.RewardType> { Ei.RewardType.UnknownReward, Ei.RewardType.Artifact }
            };
            var s = AssignmentSettingsMigration.FromLegacyKeys(a);
            CollectionAssert.DoesNotContain(s.NewContractRewardFilter, Ei.RewardType.UnknownReward);
            CollectionAssert.DoesNotContain(s.LegacyRewardFilter, Ei.RewardType.UnknownReward);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NullFilters_BecomeEmpty() {
            var s = AssignmentSettingsMigration.FromLegacyKeys(new EggIncAccount());
            Assert.IsNotNull(s.NewContractRewardFilter);
            Assert.IsNotNull(s.LegacyRewardFilter);
            Assert.AreEqual(0, s.NewContractRewardFilter.Count);
            Assert.AreEqual(0, s.LegacyRewardFilter.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_MapsFromBool() {
            Assert.AreEqual(ForceMode.AssignIfMissing, AssignmentSettingsMigration.FromLegacyKeys(new EggIncAccount { DoUnfinishedCollegtibles = true }).Get(PermanentRewardKind.Colleggtible).Mode);
            Assert.AreEqual(ForceMode.NotSet, AssignmentSettingsMigration.FromLegacyKeys(new EggIncAccount { DoUnfinishedCollegtibles = false }).Get(PermanentRewardKind.Colleggtible).Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalPe_BelowThreshold_CarriesFloor() {
            var a = new EggIncAccount { SeasonalPeOption = SeasonalPeOption.AssignIfBelowThreshold, SeasonalPeThreshold = 7000 };
            var pe = AssignmentSettingsMigration.FromLegacyKeys(a).Get(PermanentRewardKind.SeasonalPe);
            Assert.AreEqual(ForceMode.BelowThreshold, pe.Mode);
            Assert.AreEqual(7000d, pe.CsFloor);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EverySeasonalPeOption_Maps_NoNeverMode() {
            foreach(SeasonalPeOption opt in Enum.GetValues(typeof(SeasonalPeOption))) {
                var mode = AssignmentSettingsMigration.FromLegacyKeys(new EggIncAccount { SeasonalPeOption = opt }).Get(PermanentRewardKind.SeasonalPe).Mode;
                var expected = opt switch {
                    SeasonalPeOption.NotSet => ForceMode.NotSet,
                    SeasonalPeOption.AlwaysAssignIfMissing => ForceMode.AssignIfMissing,
                    SeasonalPeOption.AssignIfBelowThreshold => ForceMode.BelowThreshold,
                    SeasonalPeOption.DontAssign => ForceMode.NotSet, // skip removed -> assigned normally
                    _ => ForceMode.NotSet
                };
                Assert.AreEqual(expected, mode, $"option {opt}");
            }
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Redo_And_TwoToThree_Map() {
            var a = new EggIncAccount {
                RedoLeggacySelection = RedoLeggacyOption.YesThreshold,
                RedoScoreThreshold = 15000,
                DoTwoToThreeContracts = true
            };
            var s = AssignmentSettingsMigration.FromLegacyKeys(a);
            Assert.AreEqual(RedoLeggacyOption.YesThreshold, s.Redo.Mode);
            Assert.AreEqual(15000, s.Redo.ScoreThreshold);
            Assert.IsFalse(s.Redo.ExcludeSeasonal);
            Assert.IsTrue(s.TwoToThree);
        }
    }
}
