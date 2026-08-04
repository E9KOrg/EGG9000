using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class CoopTimingHelperTests {

        [TestMethod]
        public void GetHoursToKick_UsesGuildValues() {
            var guild = new Guild { JoinTimeHours = 8, JoinTimeUltraHours = 12 };
            Assert.AreEqual(8, CoopTimingHelper.GetHoursToKick(guild, ccOnly: false));
            Assert.AreEqual(12, CoopTimingHelper.GetHoursToKick(guild, ccOnly: true));
        }

        [TestMethod]
        public void GetHoursToKick_FallsBackToDefaultsWhenUnset() {
            var guild = new Guild { JoinTimeHours = 0, JoinTimeUltraHours = 0 };
            Assert.AreEqual(18, CoopTimingHelper.GetHoursToKick(guild, ccOnly: false));
            Assert.AreEqual(24, CoopTimingHelper.GetHoursToKick(guild, ccOnly: true));
        }

        [TestMethod]
        public void GetJoinReminderHours_ThirdAndTwoThirds() {
            var (first, second) = CoopTimingHelper.GetJoinReminderHours(18);
            Assert.AreEqual(6.0, first, 0.001);
            Assert.AreEqual(12.0, second, 0.001);
        }

        [TestMethod]
        public void EvaluateSleepDemerit_BgDisabled_KeepsEmptySilo18hMath() {
            var guild = new Guild { DisableBG = true, OfflineDemeritHours = 5 };
            // timeEmpty 19h, no demerits yet: over the 18h empty-silo threshold
            var check = CoopTimingHelper.EvaluateSleepDemerit(guild, hoursSleeping: 25, timeEmpty: 19, demeritsGiven: 0);
            Assert.IsTrue(check.ShouldDemerit);
            Assert.AreEqual(18, check.NextDemeritAtHours);
            Assert.IsFalse(check.OfflineBased);
            // timeEmpty 17h: under threshold even though offline time is high
            var under = CoopTimingHelper.EvaluateSleepDemerit(guild, hoursSleeping: 40, timeEmpty: 17, demeritsGiven: 0);
            Assert.IsFalse(under.ShouldDemerit);
        }

        [TestMethod]
        public void EvaluateSleepDemerit_BgEnabled_UsesRawOfflineHours() {
            var guild = new Guild { DisableBG = false, OfflineDemeritHours = 10 };
            // 11h offline, threshold 10h: demerit even though silos not empty (timeEmpty negative)
            var check = CoopTimingHelper.EvaluateSleepDemerit(guild, hoursSleeping: 11, timeEmpty: -2, demeritsGiven: 0);
            Assert.IsTrue(check.ShouldDemerit);
            Assert.AreEqual(10, check.NextDemeritAtHours);
            Assert.IsTrue(check.OfflineBased);
            // second demerit threshold is 20h
            var second = CoopTimingHelper.EvaluateSleepDemerit(guild, hoursSleeping: 19, timeEmpty: 10, demeritsGiven: 1);
            Assert.IsFalse(second.ShouldDemerit);
            Assert.AreEqual(20, second.NextDemeritAtHours);
        }

        [TestMethod]
        public void EvaluateSleepDemerit_BgEnabled_ZeroSettingFallsBackTo30() {
            var guild = new Guild { DisableBG = false, OfflineDemeritHours = 0 };
            var check = CoopTimingHelper.EvaluateSleepDemerit(guild, hoursSleeping: 29, timeEmpty: 29, demeritsGiven: 0);
            Assert.IsFalse(check.ShouldDemerit);
            Assert.AreEqual(30, check.NextDemeritAtHours);
        }
    }
}
