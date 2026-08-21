using Discord;

using EGG9000.Bot.Commands;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test.ComponentsV2 {
    [TestClass]
    public class ShipReturnDmPagesTests {
        private static ContainerComponent Container(MessageComponent built) => (ContainerComponent)built.Components.First();

        [TestMethod]
        public void MainMenu_Disabled_OnlyEnableButtonAndReturn() {
            var built = ShipReturnDmModule.MainMenu(new DBUser { DiscordId = 5, DMOnShipReturn = false });
            var container = Container(built);
            var buttons = container.Components.OfType<ActionRowComponent>()
                .SelectMany(r => r.Components.OfType<ButtonComponent>()).ToList();
            Assert.AreEqual(2, buttons.Count);
            Assert.AreEqual($"SRDEnable:5", buttons[0].CustomId);
            Assert.AreEqual("MCSAccounts:5", buttons[1].CustomId);
        }

        [TestMethod]
        public void MainMenu_Enabled_TimeRowsAndToggles() {
            var user = new DBUser { DiscordId = 5, DMOnShipReturn = true, ShipReturnMinutes = 15, ShipReturnDMAfterFuel = true };
            var built = ShipReturnDmModule.MainMenu(user);
            var container = Container(built);

            var sectionIds = container.Components.OfType<SectionComponent>()
                .Select(s => ((ButtonComponent)s.Accessory).CustomId).ToList();
            CollectionAssert.Contains(sectionIds, "SRDSetFueledTime:5");
            CollectionAssert.Contains(sectionIds, "SRDSetNotFueledTime:5");

            var rowButtonIds = container.Components.OfType<ActionRowComponent>()
                .SelectMany(r => r.Components.OfType<ButtonComponent>()).Select(b => b.CustomId).ToList();
            CollectionAssert.Contains(rowButtonIds, "SRDSecondDM:5");
            CollectionAssert.Contains(rowButtonIds, "SRDDisable:5");
            CollectionAssert.Contains(rowButtonIds, "MCSAccounts:5");
        }
    }
}
