using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class AssignmentSettingsSerializationTests {
        private static readonly MessagePackSerializerOptions Lz4 =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        [TestMethod]
        [TestCategory("Unit")]
        public void RoundTrips_AllFields() {
            var settings = new AssignmentSettings {
                ForceRules = new List<PermanentRewardRule> {
                    new() { Kind = PermanentRewardKind.SeasonalPe, Mode = ForceMode.BelowThreshold, CsFloor = 5000 }
                },
                NewContractRewardFilter = new List<Ei.RewardType> { Ei.RewardType.Artifact },
                LegacyRewardFilter = new List<Ei.RewardType> { Ei.RewardType.EggsOfProphecy },
                Redo = new RedoRule { Mode = RedoLeggacyOption.YesThreshold, ScoreThreshold = 20000, ExcludeSeasonal = true },
                TwoToThree = true
            };

            var bytes = MessagePackSerializer.Serialize(settings, Lz4);
            var back = MessagePackSerializer.Deserialize<AssignmentSettings>(bytes, Lz4);

            Assert.AreEqual(ForceMode.BelowThreshold, back.Get(PermanentRewardKind.SeasonalPe).Mode);
            Assert.AreEqual(5000d, back.Get(PermanentRewardKind.SeasonalPe).CsFloor);
            Assert.AreEqual(PermanentRewardKind.Colleggtible, back.Get(PermanentRewardKind.Colleggtible).Kind);
            Assert.AreEqual(ForceMode.NotSet, back.Get(PermanentRewardKind.Colleggtible).Mode);
            CollectionAssert.AreEqual(new List<Ei.RewardType> { Ei.RewardType.Artifact }, back.NewContractRewardFilter);
            CollectionAssert.AreEqual(new List<Ei.RewardType> { Ei.RewardType.EggsOfProphecy }, back.LegacyRewardFilter);
            Assert.AreEqual(RedoLeggacyOption.YesThreshold, back.Redo.Mode);
            Assert.IsTrue(back.Redo.ExcludeSeasonal);
            Assert.IsTrue(back.TwoToThree);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SetForce_AddsThenUpdates() {
            var s = new AssignmentSettings();
            s.SetForce(PermanentRewardKind.SeasonalPe, ForceMode.BelowThreshold, 5000);
            Assert.AreEqual(ForceMode.BelowThreshold, s.Get(PermanentRewardKind.SeasonalPe).Mode);
            Assert.AreEqual(5000d, s.Get(PermanentRewardKind.SeasonalPe).CsFloor);

            s.SetForce(PermanentRewardKind.SeasonalPe, ForceMode.AssignIfMissing);
            Assert.AreEqual(ForceMode.AssignIfMissing, s.Get(PermanentRewardKind.SeasonalPe).Mode);
            Assert.IsNull(s.Get(PermanentRewardKind.SeasonalPe).CsFloor);
            Assert.AreEqual(1, s.ForceRules.Count(r => r.Kind == PermanentRewardKind.SeasonalPe));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Get_ReturnsNotSetDefault_WhenKindAbsent() {
            var settings = new AssignmentSettings();
            var rule = settings.Get(PermanentRewardKind.SeasonalPe);
            Assert.AreEqual(PermanentRewardKind.SeasonalPe, rule.Kind);
            Assert.AreEqual(ForceMode.NotSet, rule.Mode);
        }
    }
}
