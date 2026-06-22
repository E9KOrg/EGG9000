using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static Ei.MissionInfo.Types;

namespace EGG9000.Test {
    [TestClass]
    public class ShipReturnDmTests {
        [TestMethod]
        public void TankCapacityLevel0() {
            Assert.AreEqual(2_000_000_000d, MissionHelpers.GetTankCapacity(0));
        }

        [TestMethod]
        public void TankCapacityLevel7() {
            Assert.AreEqual(500_000_000_000_000d, MissionHelpers.GetTankCapacity(7));
        }

        [TestMethod]
        public void TankCapacityUnknownIsZero() {
            Assert.AreEqual(0d, MissionHelpers.GetTankCapacity(99));
        }

        [TestMethod]
        public void ShipEmojiUrlForAtreggies() {
            Assert.AreEqual("https://cdn.discordapp.com/emojis/1215022229826314380.png?v=1", MissionHelpers.GetShipEmojiUrl(Spaceship.Atreggies));
        }

        [TestMethod]
        public void ShipEmojiTagForAtreggies() {
            Assert.AreEqual("<:s:1215022229826314380>", MissionHelpers.GetShipEmojiTag(Spaceship.Atreggies));
        }

        [TestMethod]
        public void ShouldMarkSentOnSuccess() {
            Assert.IsTrue(ShipReturnDmBuilder.ShouldMarkSent(EGG9000.Bot.Helpers.DiscordHelpersExt.DMResult.Success));
        }

        [TestMethod]
        public void ShouldMarkSentWhenBlocked() {
            Assert.IsTrue(ShipReturnDmBuilder.ShouldMarkSent(EGG9000.Bot.Helpers.DiscordHelpersExt.DMResult.CannotSendToUser));
        }

        [TestMethod]
        public void ShouldNotMarkSentOnTransientError() {
            Assert.IsFalse(ShipReturnDmBuilder.ShouldMarkSent(EGG9000.Bot.Helpers.DiscordHelpersExt.DMResult.DiscordError));
        }

        [TestMethod]
        public void BuildEmbedUsesRelativeTimestampWhenReturning() {
            var model = new ShipReturnDmBuilder.ShipReturnDmModel(
                Ship: Spaceship.Atreggies,
                ReturnUnix: 1_900_000_000,
                LastBackupUnix: 1_899_999_000,
                NeedsFuel: true,
                HasReturned: false,
                AccountName: "TestFarmer",
                MultiAccount: false,
                FuelTank: new System.Collections.Generic.List<ShipReturnDmBuilder.FuelLine>(),
                UserId: 123UL,
                AccountIndex: 0,
                SiteBaseUrl: "https://egg9000.com");
            var (embed, _) = ShipReturnDmBuilder.Build(model);
            StringAssert.Contains(embed.Description, "<t:1900000000:R>");
            StringAssert.Contains(embed.Description, "<t:1899999000:R>");
            StringAssert.Contains(embed.Title, "Atreggies Henliner");
        }

        [TestMethod]
        public void BuildEmbedSaysReturnedWhenHasReturned() {
            var model = new ShipReturnDmBuilder.ShipReturnDmModel(
                Ship: Spaceship.Henerprise,
                ReturnUnix: 1_900_000_000,
                LastBackupUnix: 1_899_999_000,
                NeedsFuel: false,
                HasReturned: true,
                AccountName: "TestFarmer",
                MultiAccount: false,
                FuelTank: new System.Collections.Generic.List<ShipReturnDmBuilder.FuelLine>(),
                UserId: 123UL,
                AccountIndex: 0,
                SiteBaseUrl: "https://egg9000.com");
            var (embed, _) = ShipReturnDmBuilder.Build(model);
            StringAssert.Contains(embed.Description, "has returned");
        }
    }
}
