using EGG9000.Common.Contracts;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

namespace EGG9000.Test {
    [TestClass]
    public class BoardingGroupLaunchTests {
        // Monday 2026-01-05 00:00 Pacific == 2026-01-05 08:00 UTC (standard time, no DST in January).
        private static readonly DateTimeOffset MondayMidnightPacific = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

        [TestMethod]
        public void GetLaunchInfo_Bg1_LaunchesAt9AmPacific() {
            var now = MondayMidnightPacific; // before any stage launches
            var (launched, launchTime) = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: false, targetBoardingGroup: 1, now);

            Assert.IsFalse(launched);
            Assert.AreEqual(new DateTimeOffset(2026, 1, 5, 17, 0, 0, TimeSpan.Zero), launchTime); // 9am Pacific = 17:00 UTC in January
        }

        [TestMethod]
        public void GetLaunchInfo_AfterLaunchTime_ReportsAlreadyLaunched() {
            var now = new DateTimeOffset(2026, 1, 5, 18, 0, 0, TimeSpan.Zero); // 1 hour after BG1 launch
            var (launched, _) = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: false, targetBoardingGroup: 1, now);

            Assert.IsTrue(launched);
        }

        [TestMethod]
        public void GetLaunchInfo_Bg3_NonFridayUltraCapsAtMaxStage() {
            // Monday contract, not eligible for a 4th stage even if requested.
            var now = MondayMidnightPacific;
            var bg3 = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: true, targetBoardingGroup: 3, now);
            var bg4Requested = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: true, targetBoardingGroup: 4, now);

            Assert.AreEqual(bg3.LaunchTime, bg4Requested.LaunchTime); // capped at stage 3 (index 2) since it's not a Friday launch
        }
    }
}
