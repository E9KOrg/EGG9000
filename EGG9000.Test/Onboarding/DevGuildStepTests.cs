using EGG9000.Common.Helpers;
using EGG9000.Common.Setup;
using EGG9000.Onboarding;
using EGG9000.Onboarding.Steps;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Test.Onboarding {
    [TestClass]
    [TestCategory("Unit")]
    public class DevGuildStepTests {
        private const string FakeSecretsId = "test-secrets-id";
        private const ulong ChosenGuild = 987654321098765432UL;

        private static string Path => RequiredConfig.UserSecretsPathHint(FakeSecretsId);

        private static OnboardContext Context(ulong selectedGuildId) => new() {
            Configuration = new ConfigurationBuilder().Build(),
            Options = OnboardOptions.Parse([]),
            DbFactory = null!,
            Discord = null!,
            Services = null!,
            Output = new StringWriter(),
            ReadLine = () => null!,
            SelectedGuildId = selectedGuildId,
            SelectedGuildName = "My Server"
        };

        private static DevGuildStep Step(Dictionary<string, string> files) =>
            new(FakeSecretsId,
                path => files.TryGetValue(path, out var text) ? text : null,
                (path, text) => files[path] = text);

        [TestMethod]
        public async Task Run_NoExistingValue_WritesTheSelectedGuildId() {
            var files = new Dictionary<string, string>();

            var result = await Step(files).RunAsync(Context(ChosenGuild), CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.Created, result.Outcome, result.Detail);
            var written = JsonDocument.Parse(files[Path]).RootElement;
            Assert.AreEqual(ChosenGuild.ToString(),
                written.GetProperty("ConnectionStrings").GetProperty("DevGuildId").GetString());
        }

        // The preflight scaffolds keys as empty strings, so "present but empty" is the normal state
        // this step finds and must fill in.
        [TestMethod]
        public async Task Run_ScaffoldedButEmpty_IsFilledIn() {
            var files = new Dictionary<string, string> {
                [Path] = """{"ConnectionStrings":{"DevGuildId":"","Token":"t"}}"""
            };

            var result = await Step(files).RunAsync(Context(ChosenGuild), CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.Created, result.Outcome, result.Detail);
            var written = JsonDocument.Parse(files[Path]).RootElement;
            Assert.AreEqual(ChosenGuild.ToString(),
                written.GetProperty("ConnectionStrings").GetProperty("DevGuildId").GetString());
            Assert.AreEqual("t", written.GetProperty("ConnectionStrings").GetProperty("Token").GetString());
        }

        // The operator may deliberately point at a different server from the one being seeded now.
        [TestMethod]
        public async Task Run_ExistingValue_IsNeverOverwritten() {
            var files = new Dictionary<string, string> {
                [Path] = """{"ConnectionStrings":{"DevGuildId":"111111111111111111"}}"""
            };

            var result = await Step(files).RunAsync(Context(ChosenGuild), CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.AlreadyExisted, result.Outcome, result.Detail);
            var written = JsonDocument.Parse(files[Path]).RootElement;
            Assert.AreEqual("111111111111111111",
                written.GetProperty("ConnectionStrings").GetProperty("DevGuildId").GetString());
        }

        [TestMethod]
        public async Task Run_NoGuildSelected_SkipsWithoutWriting() {
            var files = new Dictionary<string, string>();

            var result = await Step(files).RunAsync(Context(0), CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.Skipped, result.Outcome);
            Assert.AreEqual(0, files.Count);
        }

        [TestMethod]
        public async Task Run_CommentedFile_RefusesToRewriteAndSaysWhatToSet() {
            var files = new Dictionary<string, string> {
                [Path] = """
                    {
                      // notes
                      "ConnectionStrings": { "Token": "t" }
                    }
                    """
            };
            var before = files[Path];

            var result = await Step(files).RunAsync(Context(ChosenGuild), CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.Skipped, result.Outcome);
            Assert.AreEqual(before, files[Path], "A commented file must never be rewritten.");
            StringAssert.Contains(result.Detail, ChosenGuild.ToString());
        }
    }
}
