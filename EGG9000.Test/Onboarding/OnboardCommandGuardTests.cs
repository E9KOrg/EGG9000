using EGG9000.Onboarding;
using EGG9000.Onboarding.Steps;
using EGG9000.Common.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Test.Onboarding {
    [TestClass]
    [TestCategory("Unit")]
    public class OnboardCommandGuardTests {
        [TestMethod]
        public void IsBlockedConfiguration_MatchesDev9001AndRelease() {
            var expected = BuildConfig.IsDev9001 || BuildConfig.IsRelease;
            Assert.AreEqual(expected, OnboardCommand.IsBlockedConfiguration(),
                "Onboard writes to the database. DEV9001 and RELEASE point at the production database.");
        }

        [TestMethod]
        public void ProductionGuardMessage_NamesTheBlockedConfigurations() {
            StringAssert.Contains(OnboardCommand.ProductionGuardMessage, "DEV9001");
            StringAssert.Contains(OnboardCommand.ProductionGuardMessage, "DEV9002");
        }

        [TestMethod]
        public void ExitCodes_AreConventional() {
            Assert.AreEqual(0, OnboardCommand.ExitSuccess);
            Assert.AreEqual(1, OnboardCommand.ExitFailure);
        }

        // The most common first run is one with no secrets configured at all. The whole point of
        // running the preflight first is that such a run gets a readable list of missing keys and a
        // clean exit 1, rather than an exception from the database or Discord wiring. This asserts
        // RunAsync survives a completely empty configuration and never throws out of the command.
        [TestMethod]
        public async Task RunAsync_EmptyConfiguration_ReportsMissingKeysAndExitsOne() {
            if(OnboardCommand.IsBlockedConfiguration()) {
                Assert.Inconclusive("Blocked build configuration; the guard is covered by its own test.");
                return;
            }

            var emptyConfig = new ConfigurationBuilder().Build();

            var original = Console.Out;
            var captured = new StringWriter();
            Console.SetOut(captured);
            int exitCode;
            try {
                exitCode = await OnboardCommand.RunAsync(["--onboard", "--no-wait"], emptyConfig, CancellationToken.None);
            } finally {
                Console.SetOut(original);
            }

            Assert.AreEqual(OnboardCommand.ExitFailure, exitCode);
            StringAssert.Contains(captured.ToString(), "ConnectionStrings:DefaultConnection",
                "Preflight must name the missing connection string rather than failing elsewhere.");

            // This test drives the real, un-injected preflight, which scaffolds missing keys into a
            // secrets file on disk. It is safe only because no test project declares a UserSecretsId,
            // so the id resolves to null and the step writes nothing. Asserting it here means adding
            // a UserSecretsId to EGG9000.Test fails this test rather than silently letting the suite
            // start rewriting a developer's real credentials file.
            Assert.IsNull(SecretsPreflightStep.ResolveUserSecretsId(),
                "A test project must not declare a UserSecretsId; preflight would then write to a real secrets.json.");
        }
    }
}
