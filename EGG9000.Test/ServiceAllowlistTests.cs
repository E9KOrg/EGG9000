using EGG9000.Bot.Services;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ServiceAllowlistTests {
        [TestMethod]
        public void Parse_Null_IsInactive_EverythingEnabled() {
            var allowlist = ServiceAllowlist.Parse(null);

            Assert.IsFalse(allowlist.Active);
            Assert.AreEqual(0, allowlist.Entries.Count);
            Assert.IsTrue(allowlist.IsEnabled("UpdateBackups"));
            Assert.IsTrue(allowlist.IsEnabled(typeof(JobService)));
        }

        [TestMethod]
        public void Parse_Empty_IsInactive_EverythingEnabled() {
            var allowlist = ServiceAllowlist.Parse("");

            Assert.IsFalse(allowlist.Active);
            Assert.IsTrue(allowlist.IsEnabled("Anything"));
        }

        [TestMethod]
        public void Parse_WhitespaceAndCommasOnly_IsInactive() {
            var allowlist = ServiceAllowlist.Parse(" , ,, ");

            Assert.IsFalse(allowlist.Active);
            Assert.AreEqual(0, allowlist.Entries.Count);
            Assert.IsTrue(allowlist.IsEnabled("Anything"));
        }

        [TestMethod]
        public void Parse_TrimsAndIgnoresEmptyEntries() {
            var allowlist = ServiceAllowlist.Parse(" UpdateBackups , ,UserSnapShots,, ");

            Assert.IsTrue(allowlist.Active);
            Assert.AreEqual(2, allowlist.Entries.Count);
            CollectionAssert.AreEquivalent(new[] { "UpdateBackups", "UserSnapShots" }, allowlist.Entries.ToList());
            Assert.IsTrue(allowlist.IsEnabled("UpdateBackups"));
            Assert.IsTrue(allowlist.IsEnabled("UserSnapShots"));
            Assert.IsFalse(allowlist.IsEnabled("ShipReturnDM"));
            Assert.IsFalse(allowlist.IsEnabled(""));
        }

        [TestMethod]
        public void IsEnabled_IsCaseInsensitive() {
            var allowlist = ServiceAllowlist.Parse("updatebackups");

            Assert.IsTrue(allowlist.IsEnabled("UpdateBackups"));
            Assert.IsTrue(allowlist.IsEnabled("UPDATEBACKUPS"));
            Assert.IsFalse(allowlist.IsEnabled("UpdateBackup"));
        }

        [TestMethod]
        public void IsEnabled_Type_UsesSimpleName() {
            var allowlist = ServiceAllowlist.Parse("jobservice");

            Assert.IsTrue(allowlist.IsEnabled(typeof(JobService)));
            Assert.IsFalse(allowlist.IsEnabled(typeof(ServiceAllowlist)));
            Assert.IsFalse(allowlist.IsEnabled(typeof(JobService).FullName));
        }
    }
}
