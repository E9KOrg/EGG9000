using System.Collections.Generic;
using System.Linq;

using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    /// <summary>
    /// Locks the semantics of the enum-driven CoopSetting override resolution: user defaults,
    /// server force-enable, the precedence of server force-disable over everything, and the legacy
    /// per-xref opt-ins.
    /// </summary>
    [TestClass]
    public class CoopSettingResolutionTests {
        private static Guild GuildWith(params ServerCoopSetting[] settings) =>
            new() { CoopSettings = settings.ToList() };

        [TestMethod]
        [TestCategory("Unit")]
        public void User_default_enables_setting() {
            var user = new DBUser { CoopSetting = new CoopSetting { PingOnMessage = true } };
            var resolved = new CoopSetting(new UserCoopXref(), user, new Guild());
            Assert.IsTrue(resolved.PingOnMessage);
            Assert.IsFalse(resolved.PingOnFull);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Guild_force_enable_beats_user_off() {
            var user = new DBUser { CoopSetting = new CoopSetting() };
            var guild = GuildWith(new ServerCoopSetting { CoopSetting = GuildCoopSetting.PingOnFull, Enabled = true, Locked = true });
            var resolved = new CoopSetting(new UserCoopXref(), user, guild);
            Assert.IsTrue(resolved.PingOnFull);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Guild_force_disable_beats_user_and_xref() {
            var user = new DBUser { CoopSetting = new CoopSetting { PingOnFull = true } };
            var xref = new UserCoopXref { PingOnFull = true };
            var guild = GuildWith(new ServerCoopSetting { CoopSetting = GuildCoopSetting.PingOnFull, Enabled = false, Locked = true });
            var resolved = new CoopSetting(xref, user, guild);
            Assert.IsFalse(resolved.PingOnFull);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Xref_optins_layer_on_top() {
            var user = new DBUser { CoopSetting = new CoopSetting() };
            var xref = new UserCoopXref { PingOnHighestEB = true, PingOnFinished = true };
            var resolved = new CoopSetting(xref, user, new Guild());
            Assert.IsTrue(resolved.PingOnHighestEB);
            // PingOnEveryoneCheckedIn legacy-rides on xref.PingOnFinished.
            Assert.IsTrue(resolved.PingOnEveryoneCheckedIn);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PingOnCoopCreatedEvenIfJoined_is_distinct_from_PingOnCoopCreated() {
            var user = new DBUser { CoopSetting = new CoopSetting { PingOnCoopCreatedEvenIfJoined = true } };
            var resolved = new CoopSetting(new UserCoopXref(), user, new Guild());
            Assert.IsTrue(resolved.PingOnCoopCreatedEvenIfJoined);
            Assert.IsFalse(resolved.PingOnCoopCreated);
        }
    }
}
