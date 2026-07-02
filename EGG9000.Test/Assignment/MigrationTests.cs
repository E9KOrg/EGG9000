using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class MigrationTests {
        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_LegacyWinsElseMain_KeepsPeFromLegacy() {
            var a = new EggIncAccount {
                AutoRegisterRewards = new() { Ei.RewardType.EggsOfProphecy, Ei.RewardType.Gold },
                LeggacyAutoRegisterRewards = new() { Ei.RewardType.Artifact, Ei.RewardType.EggsOfProphecy }
            };
            var s = AssignmentSettingsMigration.FromLegacyKeys(a);
            // PE kept from legacy source (V1 kept it); PE still stripped when falling back to new-contract list
            CollectionAssert.AreEquivalent(new List<Ei.RewardType> { Ei.RewardType.Artifact, Ei.RewardType.EggsOfProphecy }, s.RewardFilter);

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

        // PE re-migration heal in the EggIncAccounts getter (DBUser). Rebuilds the user from the
        // persisted column so the getter's heal branch actually runs (an in-memory _accounts cache
        // would short-circuit it).
        private static DBUser Rehydrate(DBUser source) => new() {
            _eggIncIds = source._eggIncIds,
            _contractRegistrationByte = source._contractRegistrationByte
        };

        [TestMethod]
        [TestCategory("Unit")]
        public void PeHeal_AddsPeOnly_PreservesPostV2Edits_StripsLegacyKey() {
            var source = new DBUser {
                EggIncAccounts = new List<EggIncAccount> {
                    new() {
                        Id = "EI1",
                        LeggacyAutoRegisterRewards = new() { Ei.RewardType.Artifact, Ei.RewardType.EggsOfProphecy },
                        // Post-V2 edit the heal must not clobber.
                        Assignment = new AssignmentSettings { RewardFilter = new() { Ei.RewardType.Gold } }
                    }
                }
            };
            var healed = Rehydrate(source).EggIncAccounts.Single();

            CollectionAssert.AreEquivalent(
                new List<Ei.RewardType> { Ei.RewardType.Gold, Ei.RewardType.EggsOfProphecy },
                healed.Assignment.RewardFilter);
            // Legacy key stripped of PE so the heal is one-shot.
            CollectionAssert.DoesNotContain(healed.LeggacyAutoRegisterRewards, Ei.RewardType.EggsOfProphecy);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PeHeal_DoesNotResurrectPeAfterUserRemovesIt() {
            var source = new DBUser {
                EggIncAccounts = new List<EggIncAccount> {
                    new() {
                        Id = "EI1",
                        LeggacyAutoRegisterRewards = new() { Ei.RewardType.EggsOfProphecy },
                        Assignment = new AssignmentSettings { RewardFilter = new() }
                    }
                }
            };
            var afterHeal = Rehydrate(source);
            var account = afterHeal.EggIncAccounts.Single();
            CollectionAssert.Contains(account.Assignment.RewardFilter, Ei.RewardType.EggsOfProphecy);

            // User unticks PE in the new UI; the heal must not re-add it on the next load.
            account.Assignment.RewardFilter.Remove(Ei.RewardType.EggsOfProphecy);
            afterHeal.UpdateAccounts();

            var reloaded = Rehydrate(afterHeal).EggIncAccounts.Single();
            CollectionAssert.DoesNotContain(reloaded.Assignment.RewardFilter, Ei.RewardType.EggsOfProphecy);
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
