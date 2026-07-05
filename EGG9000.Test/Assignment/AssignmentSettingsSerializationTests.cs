using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class AssignmentSettingsSerializationTests {
        private static readonly MessagePackSerializerOptions Lz4 =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        [TestMethod]
        [TestCategory("Unit")]
        public void Seasonal_RoundTrips_And_DefaultsToUntilPe() {
            var s = new AssignmentSettings {
                RewardFilter = [Ei.RewardType.Gold],
                Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilCsGoal, CsGoal = 12000, RewardFilterAfter = true }
            };
            var bytes = MessagePackSerializer.Serialize(s, Lz4, TestContext.CancellationToken);
            var back = MessagePackSerializer.Deserialize<AssignmentSettings>(bytes, Lz4, TestContext.CancellationToken);

            Assert.AreEqual(SeasonalMode.UntilCsGoal, back.Seasonal.Mode);
            Assert.AreEqual(12000d, back.Seasonal.CsGoal);
            Assert.IsTrue(back.Seasonal.RewardFilterAfter);
            CollectionAssert.AreEqual(s.RewardFilter, back.RewardFilter);
            Assert.AreEqual(SeasonalMode.AlwaysAssign, new AssignmentSettings().Seasonal.Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FullModel_RoundTrips() {
            var s = new AssignmentSettings {
                RewardFilter = [Ei.RewardType.Gold, Ei.RewardType.Artifact],
                Redo = new RedoRule { Mode = RedoLeggacyOption.YesThreshold, ScoreThreshold = 33000, ExcludeSeasonal = true },
                TwoToThree = true,
                Seasonal = new SeasonalRule { Mode = SeasonalMode.AlwaysAssign, CsGoal = 5000, RewardFilterAfter = false }
            };
            s.SetForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);

            var bytes = MessagePackSerializer.Serialize(s, Lz4, TestContext.CancellationToken);
            var back = MessagePackSerializer.Deserialize<AssignmentSettings>(bytes, Lz4, TestContext.CancellationToken);

            CollectionAssert.AreEqual(s.RewardFilter, back.RewardFilter);
            Assert.AreEqual(RedoLeggacyOption.YesThreshold, back.Redo.Mode);
            Assert.AreEqual(33000, back.Redo.ScoreThreshold);
            Assert.IsTrue(back.Redo.ExcludeSeasonal);
            Assert.IsTrue(back.TwoToThree);
            Assert.AreEqual(SeasonalMode.AlwaysAssign, back.Seasonal.Mode);
            Assert.AreEqual(ForceMode.AssignIfMissing, back.Get(PermanentRewardKind.Colleggtible).Mode);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SetForce_AddsThenUpdates() {
            var s = new AssignmentSettings();

            s.SetForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);
            Assert.HasCount(1, s.ForceRules);
            Assert.AreEqual(ForceMode.AssignIfMissing, s.Get(PermanentRewardKind.Colleggtible).Mode);

            s.SetForce(PermanentRewardKind.Colleggtible, ForceMode.NotSet, csFloor: 42);
            Assert.HasCount(1, s.ForceRules, "SetForce must upsert, not append a duplicate");
            Assert.AreEqual(ForceMode.NotSet, s.Get(PermanentRewardKind.Colleggtible).Mode);
            Assert.AreEqual(42d, s.Get(PermanentRewardKind.Colleggtible).CsFloor);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Get_ReturnsDetachedDefault_WhenAbsent() {
            var s = new AssignmentSettings();
            var rule = s.Get(PermanentRewardKind.Colleggtible);
            Assert.AreEqual(ForceMode.NotSet, rule.Mode);
            Assert.IsEmpty(s.ForceRules, "Get must not mutate the list");
        }

        public TestContext TestContext { get; set; }
    }
}
