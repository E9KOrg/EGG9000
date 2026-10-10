using EGG9000.Common.Contracts;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
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
        public void GetLaunchInfo_CcOnly_HasFourthStageRegardlessOfDay() {
            // Monday (non-Friday) cc-only contract still gets a distinct BG4; cc-only always caps at 4.
            var now = MondayMidnightPacific;
            var bg3 = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: true, targetBoardingGroup: 3, now);
            var bg4 = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: true, targetBoardingGroup: 4, now);
            var bg5 = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: true, targetBoardingGroup: 5, now);

            Assert.AreNotEqual(bg3.LaunchTime, bg4.LaunchTime);
            Assert.AreEqual(bg4.LaunchTime, bg5.LaunchTime);
        }

        [TestMethod]
        public void GetLaunchInfo_NonCcOnly_CapsAtThirdStage() {
            var now = MondayMidnightPacific;
            var bg3 = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: false, targetBoardingGroup: 3, now);
            var bg4Requested = BoardingGroupLaunch.GetLaunchInfo(MondayMidnightPacific, ccOnly: false, targetBoardingGroup: 4, now);

            Assert.AreEqual(bg3.LaunchTime, bg4Requested.LaunchTime);
        }

        [TestMethod]
        public void MaxBoardingGroup_CcOnly_IsFour() => Assert.AreEqual(4, BoardingGroupLaunch.MaxBoardingGroup(true));

        [TestMethod]
        public void MaxBoardingGroup_NonCcOnly_IsThree() => Assert.AreEqual(3, BoardingGroupLaunch.MaxBoardingGroup(false));

        [TestMethod]
        public void NextPickupBoardingGroup_OwnGroupNotLaunched_ReturnsOwnGroup() {
            var now = MondayMidnightPacific; // nothing launched yet
            Assert.AreEqual(2, BoardingGroupLaunch.NextPickupBoardingGroup(MondayMidnightPacific, ccOnly: false, group: 2, now));
        }

        [TestMethod]
        public void NextPickupBoardingGroup_MissedOwnGroup_ReturnsNextUpcoming() {
            // After BG1 launched (17:00 UTC) but before BG2 (01:00 UTC next day): a BG1 user is swept at BG2.
            var now = new DateTimeOffset(2026, 1, 5, 18, 0, 0, TimeSpan.Zero);
            Assert.AreEqual(2, BoardingGroupLaunch.NextPickupBoardingGroup(MondayMidnightPacific, ccOnly: false, group: 1, now));
        }

        [TestMethod]
        public void NextPickupBoardingGroup_AllLaunched_ReturnsNull() {
            // Well after BG3 (max for non-cc-only) launched.
            var now = new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero);
            Assert.IsNull(BoardingGroupLaunch.NextPickupBoardingGroup(MondayMidnightPacific, ccOnly: false, group: 1, now));
        }
    }
}
