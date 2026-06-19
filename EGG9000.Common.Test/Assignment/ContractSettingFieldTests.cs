using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class ContractSettingFieldTests {
        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_TogglesForceRule() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, ContractSettingField.Apply(s, "colleggtible", "true").Status);
            Assert.AreEqual(ForceMode.AssignIfMissing, s.Get(PermanentRewardKind.Colleggtible).Mode);
            ContractSettingField.Apply(s, "colleggtible", "false");
            Assert.AreEqual(ForceMode.NotSet, s.Get(PermanentRewardKind.Colleggtible).Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TwoToThree_And_ExcludeSeasonal_Toggle() {
            var s = new AssignmentSettings();
            ContractSettingField.Apply(s, "twoToThree", "true");
            ContractSettingField.Apply(s, "excludeSeasonal", "true");
            Assert.IsTrue(s.TwoToThree);
            Assert.IsTrue(s.Redo.ExcludeSeasonal);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RedoMode_ValidEnum_Set_InvalidRejected() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, ContractSettingField.Apply(s, "redoMode", ((int)RedoLeggacyOption.YesAll).ToString()).Status);
            Assert.AreEqual(RedoLeggacyOption.YesAll, s.Redo.Mode);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, ContractSettingField.Apply(s, "redoMode", "999").Status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RedoThreshold_OutOfRange_BadValue() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, ContractSettingField.Apply(s, "redoThreshold", "99999").Status);
            Assert.AreEqual(ContractSettingApplyStatus.Ok, ContractSettingField.Apply(s, "redoThreshold", "20000").Status);
            Assert.AreEqual(20000, s.Redo.ScoreThreshold);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalPeMode_Maps_NoSkip() {
            var s = new AssignmentSettings();
            ContractSettingField.Apply(s, "seasonalPeMode", "1");
            Assert.AreEqual(ForceMode.AssignIfMissing, s.Get(PermanentRewardKind.SeasonalPe).Mode);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, ContractSettingField.Apply(s, "seasonalPeMode", "3").Status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalPeMode_BelowThreshold_PreservesFloor() {
            var s = new AssignmentSettings();
            ContractSettingField.Apply(s, "seasonalPeFloor", "5000");
            ContractSettingField.Apply(s, "seasonalPeMode", "2");
            Assert.AreEqual(ForceMode.BelowThreshold, s.Get(PermanentRewardKind.SeasonalPe).Mode);
            Assert.AreEqual(5000d, s.Get(PermanentRewardKind.SeasonalPe).CsFloor);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NewRewardFilter_DropsPe_KeepsOthers() {
            var s = new AssignmentSettings();
            var pe = (int)Ei.RewardType.EggsOfProphecy;
            var gold = (int)Ei.RewardType.Gold;
            Assert.AreEqual(ContractSettingApplyStatus.Ok, ContractSettingField.Apply(s, "newRewardFilter", $"{pe},{gold}").Status);
            CollectionAssert.DoesNotContain(s.NewContractRewardFilter, Ei.RewardType.EggsOfProphecy);
            CollectionAssert.Contains(s.NewContractRewardFilter, Ei.RewardType.Gold);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyRewardFilter_KeepsPe() {
            var s = new AssignmentSettings();
            ContractSettingField.Apply(s, "legacyRewardFilter", ((int)Ei.RewardType.EggsOfProphecy).ToString());
            CollectionAssert.Contains(s.LegacyRewardFilter, Ei.RewardType.EggsOfProphecy);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_Unparseable_BadValue() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, ContractSettingField.Apply(s, "newRewardFilter", "abc").Status);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UnknownField_Reported() {
            Assert.AreEqual(ContractSettingApplyStatus.UnknownField, ContractSettingField.Apply(new AssignmentSettings(), "nope", "x").Status);
        }
    }
}
