using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class ContractSettingFieldTests {
        private static ContractSettingApplyStatus Apply(AssignmentSettings s, string field, string value) =>
            ContractSettingField.Apply(s, field, value).Status;

        [TestMethod]
        [TestCategory("Unit")]
        public void Apply_HealsNullSeasonalAndRedo_FromPartialV1Blob() {
            var s = new AssignmentSettings { Seasonal = null, Redo = null };
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "seasonalMode", "0"));
            Assert.AreEqual(SeasonalMode.AlwaysAssign, s.Seasonal.Mode);
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "excludeSeasonal", "true"));
            Assert.IsTrue(s.Redo.ExcludeSeasonal);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_Bool() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "colleggtible", "true"));
            Assert.AreEqual(ForceMode.AssignIfMissing, s.Get(PermanentRewardKind.Colleggtible).Mode);
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "colleggtible", "false"));
            Assert.AreEqual(ForceMode.NotSet, s.Get(PermanentRewardKind.Colleggtible).Mode);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "colleggtible", "nope"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TwoToThree_Bool() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "twoToThree", "true"));
            Assert.IsTrue(s.TwoToThree);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "twoToThree", "x"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExcludeSeasonal_Bool() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "excludeSeasonal", "true"));
            Assert.IsTrue(s.Redo.ExcludeSeasonal);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "excludeSeasonal", ""));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RedoMode_ValidEnumElseBad() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "redoMode", ((int)RedoLeggacyOption.YesAll).ToString()));
            Assert.AreEqual(RedoLeggacyOption.YesAll, s.Redo.Mode);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "redoMode", "99"));
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "redoMode", "abc"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RedoThreshold_Bounds() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "redoThreshold", "0"));
            Assert.AreEqual(0, s.Redo.ScoreThreshold);
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "redoThreshold", "90000"));
            Assert.AreEqual(90000, s.Redo.ScoreThreshold);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "redoThreshold", "-1"));
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "redoThreshold", "90001"));
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "redoThreshold", "notnum"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalMode_ValidValuesElseBad() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "seasonalMode", "0"));
            Assert.AreEqual(SeasonalMode.AlwaysAssign, s.Seasonal.Mode);
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "seasonalMode", "1"));
            Assert.AreEqual(SeasonalMode.UntilPeEarned, s.Seasonal.Mode);
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "seasonalMode", "2"));
            Assert.AreEqual(SeasonalMode.UntilCsGoal, s.Seasonal.Mode);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "seasonalMode", "9"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalCsGoal_NonNegative() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "seasonalCsGoal", "8000"));
            Assert.AreEqual(8000d, s.Seasonal.CsGoal);
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "seasonalCsGoal", "0"));
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "seasonalCsGoal", "-5"));
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "seasonalCsGoal", "abc"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalRewardFilterAfter_Bool() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "seasonalRewardFilterAfter", "true"));
            Assert.IsTrue(s.Seasonal.RewardFilterAfter);
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "seasonalRewardFilterAfter", "maybe"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_CommaList_DropsPe() {
            var s = new AssignmentSettings();
            var pe = (int)Ei.RewardType.EggsOfProphecy;
            var gold = (int)Ei.RewardType.Gold;
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "rewardFilter", $"{pe},{gold}"));
            CollectionAssert.DoesNotContain(s.RewardFilter, Ei.RewardType.EggsOfProphecy);
            CollectionAssert.Contains(s.RewardFilter, Ei.RewardType.Gold);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_Empty_Ok() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.Ok, Apply(s, "rewardFilter", ""));
            Assert.AreEqual(0, s.RewardFilter.Count);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_Unparseable_Bad() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "rewardFilter", "2,notanumber"));
            Assert.AreEqual(ContractSettingApplyStatus.BadValue, Apply(s, "rewardFilter", "9999"));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UnknownField_Reported() {
            var s = new AssignmentSettings();
            Assert.AreEqual(ContractSettingApplyStatus.UnknownField, Apply(s, "doesNotExist", "true"));
        }
    }
}
