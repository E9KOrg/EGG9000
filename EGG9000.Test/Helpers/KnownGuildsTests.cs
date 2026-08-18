using EGG9000.Common.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace EGG9000.Test.Helpers {
    [TestClass]
    [TestCategory("Unit")]
    public class KnownGuildsTests {
        // Absent configuration must leave the shared dev server in place, so every existing checkout keeps
        // behaving exactly as it did before the key existed.
        [TestMethod]
        public void Initialize_NoValue_LeavesTheDefaultInPlace() {
            var before = KnownGuilds.Dev;

            KnownGuilds.Initialize(new ConfigurationBuilder().Build());

            Assert.AreEqual(before, KnownGuilds.Dev);
        }

        // Garbage must not silently resolve to guild 0, which would match nothing and turn every dev-guild
        // lookup into a "sequence contains no elements" crash.
        [TestMethod]
        public void Initialize_UnparseableOrZero_LeavesTheDefaultInPlace() {
            var before = KnownGuilds.Dev;

            foreach(var bad in new[] { "not-a-number", "0", "" }) {
                KnownGuilds.Initialize(Config(bad));

                Assert.AreEqual(before, KnownGuilds.Dev, $"'{bad}' must not change the dev guild.");
            }
        }

        // The production server id is a real constant and must never follow the configured dev server.
        [TestMethod]
        public void PalaceProduction_IsNotAffectedByConfiguration() {
            var original = KnownGuilds.Dev;
            try {
                KnownGuilds.Initialize(Config("123456789012345678"));

                Assert.AreEqual(656455567858073601UL, KnownGuilds.PalaceProduction);
                Assert.AreEqual(123456789012345678UL, KnownGuilds.Dev, "The dev guild should follow configuration.");
            } finally {
                // KnownGuilds holds process-wide state, so a test that changes it has to put it back or
                // every later test in the assembly inherits the change.
                KnownGuilds.Initialize(Config(original.ToString()));
            }
        }

        private static IConfiguration Config(string devGuildId) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string?>> {
                    new(KnownGuilds.DevGuildConfigKey, devGuildId)
                })
                .Build();
    }
}
