using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ExpiringShellTests {

        private static Ei.ShellObjectSpec SampleSpec() {
            return new Ei.ShellObjectSpec {
                Identifier = "shell-1",
                Name = "Test Shell",
                Price = 500,
                AssetType = Ei.ShellSpec.Types.AssetType.Coop,
                SecondsRemaining = 3600
            };
        }

        [TestMethod]
        public void ApplyDetails_SyncsEveryMirrorColumn() {
            var before = DateTimeOffset.UtcNow;
            var shell = new ExpiringShell(SampleSpec());

            Assert.AreEqual("shell-1", shell.Identifier);
            Assert.AreEqual("Test Shell", shell.Name);
            Assert.AreEqual(500u, shell.Price);
            Assert.AreEqual(Ei.ShellSpec.Types.AssetType.Coop, shell.AssetType);
            Assert.IsTrue(shell.Expires >= before.AddSeconds(3600));
            Assert.IsTrue(shell.Expires <= DateTimeOffset.UtcNow.AddSeconds(3600));
            Assert.IsNotNull(shell.Json);
        }

        [TestMethod]
        public void Details_RoundTripsFromJson() {
            var stored = new ExpiringShell(SampleSpec()).Json;
            var reloaded = new ExpiringShell { Json = stored };

            Assert.AreEqual("shell-1", reloaded.Details.Identifier);
            Assert.AreEqual(3600d, reloaded.Details.SecondsRemaining);
        }

        [TestMethod]
        public void Details_NullSafe_WithoutJson() {
            Assert.IsNull(new ExpiringShell().Details);
        }

        [TestMethod]
        public void Json_MatchesPlainSerialization() {
            var spec = SampleSpec();
            var shell = new ExpiringShell(spec);

            Assert.AreEqual(JsonConvert.SerializeObject(spec), shell.Json);
        }
    }
}
