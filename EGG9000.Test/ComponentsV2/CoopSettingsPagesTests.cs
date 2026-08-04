using Discord;

using EGG9000.Bot.Commands;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test.ComponentsV2 {
    [TestClass]
    public class CoopSettingsPagesTests {
        private static (DBUser dbuser, Guild guild) Setup() =>
            (new DBUser { DiscordId = 7, CoopSetting = new CoopSetting() }, new Guild());

        [TestMethod]
        public void MainMenu_UnlockedSettings_RowPerSettingWithToggleButton() {
            var (dbuser, guild) = Setup();
            var built = CoopSettingsModule.MainMenu(dbuser.CoopSetting, "CSAll", "Default Settings", false, false, guild, dbuser);
            var container = (ContainerComponent)built.Components.Single();
            var sections = container.Components.OfType<SectionComponent>().ToList();
            Assert.IsTrue(sections.Count >= 8); // one per unlocked GuildCoopSetting with a CoopSetting property
            Assert.IsTrue(sections.All(s => s.Accessory is ButtonComponent));
            var first = (ButtonComponent)sections[0].Accessory;
            StringAssert.StartsWith(first.CustomId, "CSAll:");
        }

        [TestMethod]
        public void MainMenu_CoopOnly_SkipsCoopCreatedSettings() {
            var (dbuser, guild) = Setup();
            var built = CoopSettingsModule.MainMenu(dbuser.CoopSetting, "CSCoopOnly", "This Co-op", true, false, guild, dbuser);
            var container = (ContainerComponent)built.Components.Single();
            var texts = container.Components.OfType<SectionComponent>()
                .Select(s => ((TextDisplayComponent)s.Components.Single()).Content).ToList();
            Assert.IsFalse(texts.Any(t => t.Contains("PingOnCoopCreated")));
        }

        [TestMethod]
        public void MainMenu_LockedSetting_TextOnlyRowNoButton() {
            var (dbuser, _) = Setup();
            var guild = new Guild { CoopSettings = [new ServerCoopSetting { CoopSetting = GuildCoopSetting.PingOnFull, Enabled = true, Locked = true }] };

            var built = CoopSettingsModule.MainMenu(dbuser.CoopSetting, "CSAll", "Default Settings", false, false, guild, dbuser);
            var container = (ContainerComponent)built.Components.Single();

            var sections = container.Components.OfType<SectionComponent>().ToList();
            Assert.IsFalse(sections.Any(s => ((ButtonComponent)s.Accessory).CustomId.StartsWith("CSAll:PingOnFull,")));

            var lockedText = container.Components.OfType<TextDisplayComponent>()
                .FirstOrDefault(t => t.Content.Contains("PingOnFull") && t.Content.Contains("Locked by Server"));
            Assert.IsNotNull(lockedText);
        }

        [TestMethod]
        public void MainMenu_OpenedFromMcs_HasContractSettingsReturn() {
            var (dbuser, guild) = Setup();
            var built = CoopSettingsModule.MainMenu(dbuser.CoopSetting, "CSAll", "Default Settings", false, true, guild, dbuser);
            var container = (ContainerComponent)built.Components.Single();
            var lastRow = (ActionRowComponent)container.Components.Last();
            var button = (ButtonComponent)lastRow.Components.Single();
            Assert.AreEqual("MCSAccounts:7", button.CustomId);
            Assert.AreEqual("← Contract Settings", button.Label);
        }
    }
}
