using EGG9000.Common.Database;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    // CS (TotalCS/SeasonCS) is no longer sourced from the protobuf backup - it is written out-of-band
    // by AccountRefresh.ApplyExtrasAsync from get_contract_player_info. Each mass UpdateBackups pass
    // rebuilds CustomBackup from the protobuf, so without carry-forward the rebuilt blob resets CS to
    // its 0 default and CSLeaderboard's "TotalCS > 0" filter drops the user until the slower CS sweep
    // runs again. Carry-forward keeps the last known CS across rebuilds.
    [TestClass]
    public class CustomBackupCarryForwardTests {
        [TestMethod]
        public void CarryForwardCs_keeps_last_value_when_fresh_has_none() {
            Assert.AreEqual(1234d, CustomBackup.CarryForwardCs(0d, 1234d));
        }

        [TestMethod]
        public void CarryForwardCs_keeps_last_value_when_fresh_negative_sentinel() {
            Assert.AreEqual(1234d, CustomBackup.CarryForwardCs(-1d, 1234d));
        }

        [TestMethod]
        public void CarryForwardCs_prefers_fresh_when_present() {
            Assert.AreEqual(5678d, CustomBackup.CarryForwardCs(5678d, 1234d));
        }

        [TestMethod]
        public void CarryForwardCs_zero_when_no_history() {
            Assert.AreEqual(0d, CustomBackup.CarryForwardCs(0d, 0d));
        }
    }
}
