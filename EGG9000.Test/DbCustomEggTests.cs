using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class DbCustomEggTests {

        private static Ei.CustomEgg SampleEgg() {
            var egg = new Ei.CustomEgg {
                Identifier = "egg-1",
                Name = "Test Egg",
                Description = "A test egg",
                Value = 12.5,
                Icon = new Ei.DLCItem { Name = "icon", Url = "https://example.test/icon.png", Checksum = "abc123" }
            };
            egg.Buffs.Add(new Ei.GameModifier { Dimension = Ei.GameModifier.Types.GameDimension.EggLayingRate, Value = 1.15, Description = "buff" });
            return egg;
        }

        [TestMethod]
        public void ApplyDetails_SyncsEveryMirrorColumn() {
            var dbEgg = new DBCustomEgg();
            dbEgg.ApplyDetails(SampleEgg());

            Assert.AreEqual("egg-1", dbEgg.Identifier);
            Assert.AreEqual("Test Egg", dbEgg.Name);
            Assert.AreEqual("A test egg", dbEgg.Description);
            Assert.AreEqual(12.5, dbEgg.Value);
            Assert.AreEqual("abc123", dbEgg.Icon.Checksum);
            Assert.AreEqual(1, dbEgg.Modifiers.Count);
            Assert.AreEqual(1.15, dbEgg.Modifiers[0].Value);
            Assert.IsNotNull(dbEgg._response);
        }

        [TestMethod]
        public void Details_RoundTripsFromBlob() {
            var dbEgg = new DBCustomEgg();
            dbEgg.ApplyDetails(SampleEgg());
            var reloaded = new DBCustomEgg { _response = dbEgg._response };

            Assert.AreEqual("egg-1", reloaded.Details.Identifier);
            Assert.AreEqual(12.5, reloaded.Details.Value);
        }

        [TestMethod]
        public void Details_NullSafe_OnPreMigrationRow() {
            Assert.IsNull(new DBCustomEgg().Details);
        }

        [TestMethod]
        public void Response_IsStableForChangeDetection() {
            var dbEgg = new DBCustomEgg();
            dbEgg.ApplyDetails(SampleEgg());

            Assert.AreEqual(JsonConvert.SerializeObject(SampleEgg()), dbEgg._response);
        }
    }
}
