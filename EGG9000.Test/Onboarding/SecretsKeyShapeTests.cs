using EGG9000.Common.Setup;
using EGG9000.Onboarding.Steps;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EGG9000.Test.Onboarding {
    // secrets.json may express a key either nested ({"ConnectionStrings":{"Token":"v"}}) or flat
    // ({"ConnectionStrings:Token":"v"}). Both are valid to IConfiguration, so setup has to cope with
    // either, including not corrupting a file that uses the flat form.
    [TestClass]
    [TestCategory("Unit")]
    public class SecretsKeyShapeTests {
        private static IConfiguration Read(string json) =>
            new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
                .Build();

        [TestMethod]
        public void Configuration_ReadsBothShapes() {
            Assert.AreEqual("v", Read("""{"ConnectionStrings":{"Token":"v"}}""")["ConnectionStrings:Token"]);
            Assert.AreEqual("v", Read("""{"ConnectionStrings:Token":"v"}""")["ConnectionStrings:Token"]);
        }

        [TestMethod]
        public void Preflight_TreatsBothShapesAsPresent() {
            foreach(var json in new[] {
                """{"ConnectionStrings":{"DefaultConnection":"c","Token":"t","ClientId":"i","ClientSecret":"s"}}""",
                """{"ConnectionStrings:DefaultConnection":"c","ConnectionStrings:Token":"t","ConnectionStrings:ClientId":"i","ConnectionStrings:ClientSecret":"s"}"""
            }) {
                var missing = RequiredConfig.MissingFor(Read(json), ConfigComponent.Both, isRelease: false);
                Assert.AreEqual(0, missing.Count, $"Nothing should be missing for: {json}");
            }
        }

        // The scaffolder splits on ':' and nests. A file already using the flat form has no
        // "ConnectionStrings" object to walk into, so this pins down whether it produces a second,
        // conflicting entry for a key that is really already there.
        [TestMethod]
        public void Scaffolder_OnAFlatFile_DoesNotProduceTwoEntriesForOneKey() {
            const string flat = """{"ConnectionStrings:Token":"real-token"}""";

            var outcome = SecretsFileScaffolder.AddMissingKeys(flat, ["ConnectionStrings:Token"]);

            var config = Read(outcome.Json);
            Assert.AreEqual("real-token", config["ConnectionStrings:Token"],
                $"The real value must survive scaffolding. Result was: {outcome.Json}");
        }

        [TestMethod]
        public void SetIfAbsent_OnAFlatFile_DoesNotOverrideAnExistingValue() {
            const string flat = """{"ConnectionStrings:DevGuildId":"111111111111111111"}""";

            var outcome = SecretsFileScaffolder.SetIfAbsent(flat, "ConnectionStrings:DevGuildId", "999");

            var config = Read(outcome.Json);
            Assert.AreEqual("111111111111111111", config["ConnectionStrings:DevGuildId"],
                $"An existing value must never be replaced. Result was: {outcome.Json}");
        }

        [TestMethod]
        public void SetIfAbsent_OnAFlatFileWithAnEmptyValue_FillsItIn() {
            const string flat = """{"ConnectionStrings:DevGuildId":""}""";

            var outcome = SecretsFileScaffolder.SetIfAbsent(flat, "ConnectionStrings:DevGuildId", "999");

            var config = Read(outcome.Json);
            Assert.AreEqual("999", config["ConnectionStrings:DevGuildId"],
                $"An empty value should be filled in. Result was: {outcome.Json}");
        }
    }
}
