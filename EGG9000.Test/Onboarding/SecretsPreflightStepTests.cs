using EGG9000.Onboarding;
using EGG9000.Onboarding.Steps;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Test.Onboarding {
    [TestClass]
    [TestCategory("Unit")]
    public class SecretsPreflightStepTests {
        private const string FakeSecretsId = "test-secrets-id";

        // Every key that is required in a dev build, so a test can assert the "nothing missing" path
        // without silently going stale when RequiredConfig gains an entry.
        private static (string, string)[] AllRequiredKeys() => [
            ("ConnectionStrings:DefaultConnection", "Host=localhost;Database=x;Username=y;Password=z"),
            ("ConnectionStrings:Token", "a-token"),
            ("ConnectionStrings:ClientId", "a-client-id"),
            ("ConnectionStrings:ClientSecret", "a-client-secret")
        ];

        private static (OnboardContext Ctx, StringWriter Out) Context(params (string Key, string Value)[] values) {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
                .Build();
            var writer = new StringWriter();
            var ctx = new OnboardContext {
                Configuration = config,
                Options = OnboardOptions.Parse([]),
                DbFactory = null!,
                Discord = null!,
                Services = null!,
                Output = writer,
                ReadLine = () => null!
            };
            return (ctx, writer);
        }

        // The step is always built with fake file access. A unit test must never write to the real
        // secrets file, which holds the developer's actual credentials.
        private static SecretsPreflightStep Step(Dictionary<string, string> files) =>
            new(FakeSecretsId,
                path => files.TryGetValue(path, out var text) ? text : null,
                (path, text) => files[path] = text);

        [TestMethod]
        public async Task Run_MissingKeys_FailsAndListsAllOfThem() {
            var (ctx, output) = Context();
            var result = await Step([]).RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.Failed, result.Outcome);
            var text = output.ToString();
            StringAssert.Contains(text, "ConnectionStrings:DefaultConnection");
            StringAssert.Contains(text, "ConnectionStrings:Token");
        }

        // The README no longer lists any key names, so preflight is the only thing that can tell a
        // developer the site's OAuth keys exist. Reporting only the bot's keys would leave them
        // unable to complete setup.
        [TestMethod]
        public async Task Run_MissingKeys_AlsoReportsTheSiteOAuthKeys() {
            var (ctx, output) = Context();
            await Step([]).RunAsync(ctx, CancellationToken.None);

            var text = output.ToString();
            StringAssert.Contains(text, "ConnectionStrings:ClientId");
            StringAssert.Contains(text, "ConnectionStrings:ClientSecret");
        }

        [TestMethod]
        public async Task Run_MissingKeys_PrintsSecretsJsonPath() {
            var (ctx, output) = Context();
            await Step([]).RunAsync(ctx, CancellationToken.None);

            var text = output.ToString();
            StringAssert.Contains(text, "secrets.json");
            StringAssert.Contains(text, FakeSecretsId);
        }

        [TestMethod]
        public async Task Run_MissingKeys_WritesThemIntoSecretsJsonAsEmptyStrings() {
            var (ctx, _) = Context();
            var files = new Dictionary<string, string>();

            await Step(files).RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(1, files.Count, "Exactly one secrets file should have been written.");
            var written = JsonDocument.Parse(files.Values.Single()).RootElement;
            var connectionStrings = written.GetProperty("ConnectionStrings");
            Assert.AreEqual("", connectionStrings.GetProperty("Token").GetString());
            Assert.AreEqual("", connectionStrings.GetProperty("ClientSecret").GetString());
        }

        // Only the bot's own keys stop the run. The site keys are needed later, so blocking on them
        // would prevent setup from doing the database work it is perfectly able to do now.
        [TestMethod]
        public async Task Run_OnlySiteKeysMissing_ReportsWithoutFailing() {
            var (ctx, _) = Context(
                ("ConnectionStrings:DefaultConnection", "Host=localhost;Database=x;Username=y;Password=z"),
                ("ConnectionStrings:Token", "a-token"));

            var result = await Step([]).RunAsync(ctx, CancellationToken.None);

            Assert.AreNotEqual(OnboardOutcome.Failed, result.Outcome, result.Detail);
        }

        [TestMethod]
        public async Task Run_AllKeysPresent_Succeeds() {
            var (ctx, _) = Context(AllRequiredKeys());
            var files = new Dictionary<string, string>();

            var result = await Step(files).RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.AlreadyExisted, result.Outcome);
            Assert.AreEqual(0, files.Count, "Nothing was missing, so no file should have been written.");
        }

        // A file whose keys are all present but empty needs no write at all, so the comment refusal
        // must not fire and tell the operator to add keys that are already there.
        [TestMethod]
        public async Task Run_KeysAlreadyPresentButEmptyInACommentedFile_DoesNotClaimTheyAreMissingFromIt() {
            var (ctx, output) = Context();
            var path = EGG9000.Common.Setup.RequiredConfig.UserSecretsPathHint(FakeSecretsId);
            var files = new Dictionary<string, string> {
                [path] = """
                    {
                      // dev database
                      "ConnectionStrings": {
                        "DefaultConnection": "", "Token": "", "ClientId": "", "ClientSecret": ""
                      }
                    }
                    """
            };
            var before = files[path];

            await Step(files).RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(before, files[path], "Nothing needed adding, so the file must be untouched.");
            StringAssert.Contains(output.ToString(), "already present but empty");
            Assert.IsFalse(output.ToString().Contains("by hand"),
                "Nothing needed writing, so the operator must not be told to add keys by hand.");
        }

        // Rewriting cannot preserve comments, so a file that has them is left alone rather than
        // silently stripped of the developer's notes about their own credentials.
        [TestMethod]
        public async Task Run_CommentedFileMissingAKey_RefusesToRewriteIt() {
            var (ctx, output) = Context();
            var path = EGG9000.Common.Setup.RequiredConfig.UserSecretsPathHint(FakeSecretsId);
            var files = new Dictionary<string, string> {
                [path] = """{ /* notes */ "ConnectionStrings": { "Token": "" } }"""
            };
            var before = files[path];

            await Step(files).RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(before, files[path], "A commented file must never be rewritten.");
            StringAssert.Contains(output.ToString(), "comments");
        }

        [TestMethod]
        public async Task Run_UnreadableFile_ReportsItInsteadOfThrowing() {
            var (ctx, output) = Context();
            var step = new SecretsPreflightStep(
                FakeSecretsId,
                _ => throw new IOException("The process cannot access the file."),
                (_, _) => Assert.Fail("Nothing should be written when the file cannot be read."));

            var result = await step.RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.Failed, result.Outcome, "Bot keys are missing, so the run still stops.");
            StringAssert.Contains(output.ToString(), "could not be read");
        }

        // "created" must never appear against a run that wrote nothing.
        [TestMethod]
        public async Task Run_OnlySiteKeysMissingButNothingWritten_ReportsSkippedNotCreated() {
            var (ctx, _) = Context(
                ("ConnectionStrings:DefaultConnection", "Host=localhost;Database=x;Username=y;Password=z"),
                ("ConnectionStrings:Token", "a-token"));
            var path = EGG9000.Common.Setup.RequiredConfig.UserSecretsPathHint(FakeSecretsId);
            var files = new Dictionary<string, string> {
                [path] = """{ /* notes */ "ConnectionStrings": { "ClientId2": "" } }"""
            };

            var result = await Step(files).RunAsync(ctx, CancellationToken.None);

            Assert.AreEqual(OnboardOutcome.Skipped, result.Outcome, result.Detail);
        }

        [TestMethod]
        public void Name_IsSet() {
            Assert.IsFalse(string.IsNullOrWhiteSpace(Step([]).Name));
        }
    }

    [TestClass]
    [TestCategory("Unit")]
    public class SecretsFileScaffolderTests {
        [TestMethod]
        public void AddMissingKeys_EmptyDocument_NestsOnColons() {
            var outcome = SecretsFileScaffolder.AddMissingKeys("", ["ConnectionStrings:Token"]);

            CollectionAssert.AreEqual(new[] { "ConnectionStrings:Token" }, outcome.Added.ToArray());
            var root = JsonDocument.Parse(outcome.Json).RootElement;
            Assert.AreEqual("", root.GetProperty("ConnectionStrings").GetProperty("Token").GetString());
        }

        // This file holds real credentials. Scaffolding must be purely additive or it becomes a way
        // for setup to destroy the developer's working configuration.
        [TestMethod]
        public void AddMissingKeys_ExistingValue_IsNeverOverwritten() {
            const string existing = """{"ConnectionStrings":{"Token":"real-token"}}""";

            var outcome = SecretsFileScaffolder.AddMissingKeys(existing, ["ConnectionStrings:Token"]);

            Assert.AreEqual(0, outcome.Added.Count);
            var root = JsonDocument.Parse(outcome.Json).RootElement;
            Assert.AreEqual("real-token", root.GetProperty("ConnectionStrings").GetProperty("Token").GetString());
        }

        [TestMethod]
        public void AddMissingKeys_UnrelatedContent_IsPreserved() {
            const string existing = """{"ConnectionStrings":{"Token":"real-token"},"Other":{"Thing":42}}""";

            var outcome = SecretsFileScaffolder.AddMissingKeys(existing, ["ConnectionStrings:ClientId"]);

            var root = JsonDocument.Parse(outcome.Json).RootElement;
            Assert.AreEqual("real-token", root.GetProperty("ConnectionStrings").GetProperty("Token").GetString());
            Assert.AreEqual(42, root.GetProperty("Other").GetProperty("Thing").GetInt32());
            Assert.AreEqual("", root.GetProperty("ConnectionStrings").GetProperty("ClientId").GetString());
        }

        // An empty string is what makes a scaffolded key still count as missing on the next run. A
        // descriptive placeholder would satisfy the preflight check and hide the unfilled key.
        [TestMethod]
        public void AddMissingKeys_Placeholder_IsEmptySoTheKeyStillReadsAsMissing() {
            var outcome = SecretsFileScaffolder.AddMissingKeys("", ["ConnectionStrings:ClientId"]);

            var config = new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(outcome.Json)))
                .Build();

            Assert.IsTrue(string.IsNullOrWhiteSpace(config["ConnectionStrings:ClientId"]));
        }

        [TestMethod]
        public void AddMissingKeys_MalformedDocument_ThrowsRatherThanReplacingIt() {
            // Not ThrowsExactly: the parser raises JsonReaderException, which derives from
            // JsonException and is what the step's catch clause is written against.
            Assert.Throws<JsonException>(
                () => SecretsFileScaffolder.AddMissingKeys("{ not json", ["ConnectionStrings:Token"]));
        }

        // The JSON configuration provider accepts comments and trailing commas, so a secrets.json
        // using them works at runtime. Parsing more strictly than the app does would reject a file
        // the developer has every reason to believe is fine.
        [TestMethod]
        public void AddMissingKeys_CommentsAndTrailingCommas_AreAccepted() {
            const string existing = """
                {
                  // the dev database
                  "ConnectionStrings": { "Token": "real-token", },
                }
                """;

            var outcome = SecretsFileScaffolder.AddMissingKeys(existing, ["ConnectionStrings:ClientId"]);

            Assert.IsTrue(outcome.ContainsComments, "The comment must be detected so the caller can refuse to rewrite.");
            var root = JsonDocument.Parse(outcome.Json).RootElement;
            Assert.AreEqual("real-token", root.GetProperty("ConnectionStrings").GetProperty("Token").GetString());
        }

        [TestMethod]
        public void ContainsComments_PlainDocument_IsFalse() {
            Assert.IsFalse(SecretsFileScaffolder.ContainsComments("""{"ConnectionStrings":{"Token":"x"}}"""));
            Assert.IsFalse(SecretsFileScaffolder.ContainsComments(""));
        }

        // A flat "ConnectionStrings" string leaves nowhere to nest Token under. That is not the same
        // as the key already being present, and the operator has to be told which one happened.
        [TestMethod]
        public void AddMissingKeys_ParentHoldsANonObject_ReportsBlockedRatherThanAdded() {
            const string existing = """{"ConnectionStrings":"oops-this-is-a-string"}""";

            var outcome = SecretsFileScaffolder.AddMissingKeys(existing, ["ConnectionStrings:Token"]);

            Assert.AreEqual(0, outcome.Added.Count);
            CollectionAssert.AreEqual(new[] { "ConnectionStrings:Token" }, outcome.Blocked.ToArray());
            var root = JsonDocument.Parse(outcome.Json).RootElement;
            Assert.AreEqual("oops-this-is-a-string", root.GetProperty("ConnectionStrings").GetString());
        }
    }
}
