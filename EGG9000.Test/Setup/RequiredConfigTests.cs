using EGG9000.Common.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test.Setup {
    [TestClass]
    [TestCategory("Unit")]
    public class RequiredConfigTests {
        private static IConfiguration Config(params (string Key, string Value)[] values) {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
                .Build();
        }

        [TestMethod]
        public void MissingFor_EmptyConfig_ReportsBotAlwaysKeys() {
            var missing = RequiredConfig.MissingFor(Config(), ConfigComponent.Bot, isRelease: false);
            var keys = missing.Select(m => m.ConfigKey).ToList();
            CollectionAssert.Contains(keys, "ConnectionStrings:DefaultConnection");
            CollectionAssert.Contains(keys, "ConnectionStrings:Token");
        }

        [TestMethod]
        public void MissingFor_SatisfiedKeys_AreNotReported() {
            var config = Config(
                ("ConnectionStrings:DefaultConnection", "Host=localhost;Database=x;Username=y;Password=z"),
                ("ConnectionStrings:Token", "a-token"));
            var missing = RequiredConfig.MissingFor(config, ConfigComponent.Bot, isRelease: false);
            var keys = missing.Select(m => m.ConfigKey).ToList();
            CollectionAssert.DoesNotContain(keys, "ConnectionStrings:DefaultConnection");
            CollectionAssert.DoesNotContain(keys, "ConnectionStrings:Token");
        }

        [TestMethod]
        public void MissingFor_ReleaseOnlyKeys_OnlyReportedInRelease() {
            var debugMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Bot, isRelease: false)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.DoesNotContain(debugMissing, "ConnectionStrings:ApiSalt");

            var releaseMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Bot, isRelease: true)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.Contains(releaseMissing, "ConnectionStrings:ApiSalt");
        }

        [TestMethod]
        public void MissingFor_OptionalKeys_AreNeverReported() {
            var releaseMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Bot, isRelease: true)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.DoesNotContain(releaseMissing, "ConnectionStrings:BusControlSecret");
            CollectionAssert.DoesNotContain(releaseMissing, "ConnectionStrings:CPGuildId");
        }

        [TestMethod]
        public void MissingFor_SiteOnlyKeys_AreNotReportedForBot() {
            var botMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Bot, isRelease: false)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.DoesNotContain(botMissing, "ConnectionStrings:ClientSecret");

            var siteMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Site, isRelease: false)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.Contains(siteMissing, "ConnectionStrings:ClientSecret");
        }

        [TestMethod]
        public void MissingFor_BugSnagApiKey_OnlyReportedInReleaseForBot() {
            var debugMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Bot, isRelease: false)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.DoesNotContain(debugMissing, "ConnectionStrings:BugSnagApiKey");

            var releaseMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Bot, isRelease: true)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.Contains(releaseMissing, "ConnectionStrings:BugSnagApiKey");
        }

        [TestMethod]
        public void MissingFor_DataProtectionKeys_ReportedForSiteInRelease() {
            var siteReleaseMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Site, isRelease: true)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.Contains(siteReleaseMissing, "DataProtection:CertPath");
            CollectionAssert.Contains(siteReleaseMissing, "DataProtection:CertPassword");

            var siteDebugMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Site, isRelease: false)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.DoesNotContain(siteDebugMissing, "DataProtection:CertPath");
            CollectionAssert.DoesNotContain(siteDebugMissing, "DataProtection:CertPassword");

            var botReleaseMissing = RequiredConfig.MissingFor(Config(), ConfigComponent.Bot, isRelease: true)
                .Select(m => m.ConfigKey).ToList();
            CollectionAssert.DoesNotContain(botReleaseMissing, "DataProtection:CertPath");
            CollectionAssert.DoesNotContain(botReleaseMissing, "DataProtection:CertPassword");
        }

        [TestMethod]
        public void All_EntriesHaveNonEmptyPurpose() {
            foreach(var entry in RequiredConfig.All) {
                Assert.IsFalse(string.IsNullOrWhiteSpace(entry.Purpose),
                    $"{entry.ConfigKey} has no Purpose. Preflight prints Purpose to explain what a key is for.");
            }
        }
    }
}
