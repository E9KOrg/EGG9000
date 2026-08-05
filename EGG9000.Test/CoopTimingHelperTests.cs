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

        [TestMethod]
        public void EvaluateSleepAlert_BgEnabled_FiresAtConfiguredHour() {
            var guild = new Guild { DisableBG = false, OfflineWarningHours = 22, OfflineDemeritHours = 30 };
            var at = CoopTimingHelper.EvaluateSleepAlert(guild, hoursSleeping: 22, siloTimeHours: 4);
            Assert.IsTrue(at.ShouldAlert);
            Assert.AreEqual(22.0, at.AlertAtHours, 0.001);
            Assert.AreEqual(30.0, at.DemeritAtHours, 0.001);
            Assert.IsTrue(at.OfflineBased);

            var under = CoopTimingHelper.EvaluateSleepAlert(guild, hoursSleeping: 21.9, siloTimeHours: 4);
            Assert.IsFalse(under.ShouldAlert);
        }

        [TestMethod]
        public void EvaluateSleepAlert_BgEnabled_ZeroSettingFallsBackTo22() {
            var guild = new Guild { DisableBG = false, OfflineWarningHours = 0, OfflineDemeritHours = 30 };
            var check = CoopTimingHelper.EvaluateSleepAlert(guild, hoursSleeping: 22, siloTimeHours: 0);
            Assert.IsTrue(check.ShouldAlert);
            Assert.AreEqual(22.0, check.AlertAtHours, 0.001);
        }

        [TestMethod]
        public void EvaluateSleepAlert_BgEnabled_ScalesWithLowThreshold() {
            var guild = new Guild { DisableBG = false, OfflineWarningHours = 9, OfflineDemeritHours = 12 };
            var check = CoopTimingHelper.EvaluateSleepAlert(guild, hoursSleeping: 9, siloTimeHours: 2);
            Assert.IsTrue(check.ShouldAlert);
            Assert.AreEqual(9.0, check.AlertAtHours, 0.001);
            Assert.AreEqual(12.0, check.DemeritAtHours, 0.001);
        }

        [TestMethod]
        public void EvaluateSleepAlert_BgDisabled_KeepsLegacySiloMidpoint() {
            var guild = new Guild { DisableBG = true, OfflineWarningHours = 5, OfflineDemeritHours = 5 };
            // Legacy: midpoint between silo runout (10h) and the 30h ceiling.
            var check = CoopTimingHelper.EvaluateSleepAlert(guild, hoursSleeping: 20, siloTimeHours: 10);
            Assert.IsTrue(check.ShouldAlert);
            Assert.AreEqual(20.0, check.AlertAtHours, 0.001);
            Assert.AreEqual(28.0, check.DemeritAtHours, 0.001);
            Assert.IsFalse(check.OfflineBased);

            var under = CoopTimingHelper.EvaluateSleepAlert(guild, hoursSleeping: 19.9, siloTimeHours: 10);
            Assert.IsFalse(under.ShouldAlert);
        }
    }
}
