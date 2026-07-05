using EGG9000.Site.Controllers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class AdminApiKeyUsageTests {
        [TestMethod]
        public void BelowFiveTimesBaseline_IsNotSpike() {
            Assert.IsFalse(AdminController.ComputeIsSpike(todayCount: 200, baselineAverage: 50));
        }

        [TestMethod]
        public void AboveFiveTimesBaseline_IsSpike() {
            Assert.IsTrue(AdminController.ComputeIsSpike(todayCount: 251, baselineAverage: 50));
        }

        [TestMethod]
        public void ExactlyFiveTimesBaseline_IsNotSpike() {
            Assert.IsFalse(AdminController.ComputeIsSpike(todayCount: 250, baselineAverage: 50));
        }

        [TestMethod]
        public void LowBaselineUsesFloorOfFifty() {
            // baseline of 2 -> floor kicks in at 50, so 10 requests today (5x the raw baseline) is NOT a spike.
            Assert.IsFalse(AdminController.ComputeIsSpike(todayCount: 10, baselineAverage: 2));
        }

        [TestMethod]
        public void LowBaselineStillFlagsRealSpike() {
            // 300 requests against a near-zero baseline still exceeds the 50-floor x5 threshold.
            Assert.IsTrue(AdminController.ComputeIsSpike(todayCount: 300, baselineAverage: 2));
        }
    }
}
