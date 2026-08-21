using Discord;

using EGG9000.Bot.Commands;
using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord.ComponentsV2;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test.ComponentsV2 {
    [TestClass]
    public class ContractSettingsPagesTests {
        internal static DBUser TwoAccountUser() => new() {
            DiscordId = 999,
            EggIncAccounts = [
                new() { Id = "EI1", Backup = new CustomBackup { UserName = "Foo" } },
                new() { Id = "EI2", Backup = new CustomBackup { UserName = "Bar" } },
            ]
        };

        [TestMethod]
        public void GetAccountButtons_ContainerWithHeaderAndManageRows() {
            var built = ContractSettingsCommands.GetAccountButtons(TwoAccountUser(), "MCSMenu");
            var container = (ContainerComponent)built.Components.Single();
            var header = (TextDisplayComponent)container.Components.First();
            Assert.AreEqual("# Contract Settings", header.Content);
            var sections = container.Components.OfType<SectionComponent>().ToList();
            Assert.AreEqual(2, sections.Count);
            Assert.AreEqual("MCSMenu:0,999", ((ButtonComponent)sections[0].Accessory).CustomId);
            Assert.AreEqual("MCSMenu:1,999", ((ButtonComponent)sections[1].Accessory).CustomId);
        }

        [TestMethod]
        public void GetAccountButtons_CrossFlowButtonsPresent() {
            var built = ContractSettingsCommands.GetAccountButtons(TwoAccountUser(), "MCSMenu");
            var container = (ContainerComponent)built.Components.Single();
            var row = container.Components.OfType<ActionRowComponent>().Single();
            var ids = row.Components.OfType<ButtonComponent>().Select(b => b.CustomId).ToList();
            CollectionAssert.AreEqual(new[] { "CSAccountMenu:999,true,false", "SRDMenu:999" }, ids);
        }

        private static DBUser AccountsUser(int count) => new() {
            DiscordId = 999,
            EggIncAccounts = [.. Enumerable.Range(0, count).Select(i => new EggIncAccount { Id = $"EI{i}", Backup = new CustomBackup { UserName = $"Acc{i}" } })]
        };

        [TestMethod]
        public void GetAccountButtons_AtMaxAccounts_BuildsSuccessfully() {
            var built = ContractSettingsCommands.GetAccountButtons(AccountsUser(ContractSettingsCommands.MaxAccountsForPicker), "MCSMenu");
            var container = (ContainerComponent)built.Components.Single();
            var sections = container.Components.OfType<SectionComponent>().ToList();
            Assert.AreEqual(ContractSettingsCommands.MaxAccountsForPicker, sections.Count);
        }

        [TestMethod]
        public void GetAccountButtons_OverMaxAccounts_ReturnsFriendlyError() {
            var built = ContractSettingsCommands.GetAccountButtons(AccountsUser(ContractSettingsCommands.MaxAccountsForPicker + 1), "MCSMenu");
            var container = (ContainerComponent)built.Components.Single();
            var section = container.Components.OfType<SectionComponent>().Single();
            var text = (TextDisplayComponent)section.Components.Single();
            StringAssert.Contains(text.Content, "too many linked accounts");
        }

        [TestMethod]
        public void UBGComponents_SelectAndReturn_InsideContainer() {
            var dbuser = new DBUser { DiscordId = 1, EggIncAccounts = [new() { Id = "EI1", UltraGroup = 2 }] };
            var built = ContractSettingsCommands.UBGComponents(dbuser, dbuser.EggIncAccounts[0], 0);
            var container = (ContainerComponent)built.Components.Single();
            var rows = container.Components.OfType<ActionRowComponent>().ToList();
            Assert.AreEqual(2, rows.Count); // select row + Return row
            var select = (SelectMenuComponent)rows[0].Components.Single();
            Assert.AreEqual("MCSUBoardingGroup:0,1", select.CustomId);
            Assert.AreEqual(4, select.Options.Count);
        }

        private static (DBUser dbuser, EggIncAccount account) BasicAccount() {
            var dbuser = new DBUser { DiscordId = 1, EggIncAccounts = [new() { Id = "EI1" }] };
            var account = dbuser.EggIncAccounts[0];
            account.Assignment = new EGG9000.Common.Contracts.Assignment.AssignmentSettings();
            return (dbuser, account);
        }

        [TestMethod]
        public void MainMenu_BasicAccount_HasCoreRowsAndReturn() {
            var (dbuser, account) = BasicAccount();
            var guild = new Guild { DisableBG = true, AllowGuilds = false };

            var built = ContractSettingsCommands.MainMenu(dbuser, account, 0, guild);
            var container = (ContainerComponent)built.Components.Single();

            var accessoryIds = container.Components.OfType<SectionComponent>()
                .Select(s => ((ButtonComponent)s.Accessory).CustomId).ToList();
            CollectionAssert.Contains(accessoryIds, "MCSBreak:0,1");
            CollectionAssert.Contains(accessoryIds, "MCSRewards:0,1");
            CollectionAssert.Contains(accessoryIds, "MCSSeasonalPe:0,1");
            CollectionAssert.Contains(accessoryIds, "MCSRL:0,1");
            CollectionAssert.Contains(accessoryIds, "MCSTwoToThree:0,1");
            CollectionAssert.Contains(accessoryIds, "MCSColleggtible:0,1");
            CollectionAssert.Contains(accessoryIds, "MCSUltraPing:0,1"); // no active subscription
            CollectionAssert.DoesNotContain(accessoryIds, "MCSBg:0,1");    // BG disabled
            CollectionAssert.DoesNotContain(accessoryIds, "MCSGuild:0,1"); // guilds disabled

            var lastRow = (ActionRowComponent)container.Components.Last();
            Assert.AreEqual("MCSAccounts:1", ((ButtonComponent)lastRow.Components.Single()).CustomId);
        }

        [TestMethod]
        public void MainMenu_BgEnabled_ShowsBoardingGroupRow() {
            var (dbuser, account) = BasicAccount();
            account.Group = 2;
            var guild = new Guild { DisableBG = false, AllowGuilds = true };

            var built = ContractSettingsCommands.MainMenu(dbuser, account, 0, guild);
            var container = (ContainerComponent)built.Components.Single();
            var sections = container.Components.OfType<SectionComponent>().ToList();

            var bgSection = sections.Single(s => ((ButtonComponent)s.Accessory).CustomId == "MCSBg:0,1");
            var bgText = ((TextDisplayComponent)bgSection.Components.Single()).Content;
            StringAssert.Contains(bgText, "BG2");
            Assert.IsTrue(sections.Any(s => ((ButtonComponent)s.Accessory).CustomId == "MCSGuild:0,1"));
        }

        [TestMethod]
        public void MainMenu_ExtraWarning_RedAccentAndWarningText() {
            var (dbuser, account) = BasicAccount();
            var guild = new Guild { DisableBG = true, AllowGuilds = false };

            var built = ContractSettingsCommands.MainMenu(dbuser, account, 0, guild, "custom warning text");
            var container = (ContainerComponent)built.Components.Single();
            Assert.AreEqual((Color)Color.Red, container.AccentColor);
            Assert.IsTrue(container.Components.OfType<TextDisplayComponent>().Any(t => t.Content.Contains("custom warning text")));
        }

        [TestMethod]
        public void BGComponents_SelectAndReturn_InsideContainer() {
            var dbuser = new DBUser { DiscordId = 1, EggIncAccounts = [new() { Id = "EI1", Group = 1 }] };
            var built = ContractSettingsCommands.BGComponents(dbuser, dbuser.EggIncAccounts[0], 0);
            var container = (ContainerComponent)built.Components.Single();
            var rows = container.Components.OfType<ActionRowComponent>().ToList();
            Assert.AreEqual(2, rows.Count); // select row + Return row
            var select = (SelectMenuComponent)rows[0].Components.Single();
            Assert.AreEqual("MCSBoardingGroup:0,1", select.CustomId);
            Assert.AreEqual(3, select.Options.Count);
        }

        [TestMethod]
        public void RedoLeggaciesComponents_ThresholdMode_ShowsChangeThresholdAccessory() {
            var (dbuser, account) = BasicAccount();
            account.Assignment.Redo.Mode = RedoLeggacyOption.YesThreshold;
            account.Assignment.Redo.ScoreThreshold = 5000;

            var built = ContractSettingsCommands.RedoLeggaciesComponents(dbuser, account, 0);
            var container = (ContainerComponent)built.Components.Single();
            var sections = container.Components.OfType<SectionComponent>().ToList();
            Assert.IsTrue(sections.Any(s => ((ButtonComponent)s.Accessory).CustomId == "RLThreshModal:0,1"));
            Assert.IsTrue(sections.Any(s => ((ButtonComponent)s.Accessory).CustomId == "MCSExcludeSeasonal:0,1"));
        }

        [TestMethod]
        public void RedoLeggaciesComponents_NotSetMode_NoSections() {
            var (dbuser, account) = BasicAccount();

            var built = ContractSettingsCommands.RedoLeggaciesComponents(dbuser, account, 0);
            var container = (ContainerComponent)built.Components.Single();
            Assert.AreEqual(0, container.Components.OfType<SectionComponent>().Count());
        }

        [TestMethod]
        public void SeasonalComponents_UntilCsGoalMode_ShowsGoalAndFilterAfterSections() {
            var (dbuser, account) = BasicAccount();
            account.Assignment.Seasonal = new EGG9000.Common.Contracts.Assignment.SeasonalRule { Mode = SeasonalMode.UntilCsGoal, CsGoal = 90_000 };

            var built = ContractSettingsCommands.SeasonalComponents(dbuser, account, 0);
            var container = (ContainerComponent)built.Components.Single();
            var ids = container.Components.OfType<SectionComponent>().Select(s => ((ButtonComponent)s.Accessory).CustomId).ToList();
            CollectionAssert.Contains(ids, "SeasonalPeThreshModal:0,1");
            CollectionAssert.Contains(ids, "MCSSeasonalFilterAfter:0,1");
        }

        [TestMethod]
        public void SeasonalComponents_AlwaysAssignMode_NoSections() {
            var (dbuser, account) = BasicAccount();

            var built = ContractSettingsCommands.SeasonalComponents(dbuser, account, 0);
            var container = (ContainerComponent)built.Components.Single();
            Assert.AreEqual(0, container.Components.OfType<SectionComponent>().Count());
        }

        [TestMethod]
        public void TwoToThreeComponents_Enabled_ShowsDisableButton() {
            var (dbuser, account) = BasicAccount();
            var built = ContractSettingsCommands.TwoToThreeComponents(dbuser, account, true, 0);
            var container = (ContainerComponent)built.Components.Single();
            var accessory = (ButtonComponent)container.Components.OfType<SectionComponent>().Single().Accessory;
            Assert.AreEqual("Disable 2 -> 3 Auto-Assignments", accessory.Label);
            Assert.AreEqual("MCSToggleTwoToThree:0,1,f", accessory.CustomId);
        }

        [TestMethod]
        public void UltraPingComponents_Disabled_ShowsEnableButton() {
            var (dbuser, account) = BasicAccount();
            var built = ContractSettingsCommands.UltraPingComponents(dbuser, account, false, 0);
            var container = (ContainerComponent)built.Components.Single();
            var accessory = (ButtonComponent)container.Components.OfType<SectionComponent>().Single().Accessory;
            Assert.AreEqual("Enable Pings", accessory.Label);
            Assert.AreEqual("MCSUltraPingToggle:0,1,t", accessory.CustomId);
        }

        [TestMethod]
        public void BreakComponents_FreshAccount_AddButtonsNoStopEarly() {
            var (dbuser, account) = BasicAccount();
            var built = ContractSettingsCommands.BreakComponents(dbuser, account, 0);
            var container = (ContainerComponent)built.Components.Single();
            var labels = container.Components.OfType<ActionRowComponent>()
                .SelectMany(r => r.Components.OfType<ButtonComponent>()).Select(b => b.Label).ToList();
            CollectionAssert.Contains(labels, "Add 1 Day to Break");
            CollectionAssert.Contains(labels, "Add 1 Week to Break");
            CollectionAssert.DoesNotContain(labels, "Stop Break Early");
        }

        [TestMethod]
        public void BreakComponents_ActiveBreak_ShowsStopBreakEarly() {
            var (dbuser, account) = BasicAccount();
            account.OnBreakUntil = System.DateTimeOffset.UtcNow.AddDays(3);
            var built = ContractSettingsCommands.BreakComponents(dbuser, account, 0);
            var container = (ContainerComponent)built.Components.Single();
            var labels = container.Components.OfType<ActionRowComponent>()
                .SelectMany(r => r.Components.OfType<ButtonComponent>()).Select(b => b.Label).ToList();
            CollectionAssert.Contains(labels, "Stop Break Early");
        }

        [TestMethod]
        public void RewardsComponents_FilterSet_ShowsClearButton() {
            var (dbuser, account) = BasicAccount();
            account.Assignment.RewardFilter = [Ei.RewardType.Cash];
            var built = ContractSettingsCommands.RewardsComponents(dbuser, account, 0);
            var container = (ContainerComponent)built.Components.Single();
            var labels = container.Components.OfType<ActionRowComponent>()
                .SelectMany(r => r.Components.OfType<ButtonComponent>()).Select(b => b.Label).ToList();
            CollectionAssert.Contains(labels, "Clear Filter (Do all contracts)");
        }

        [TestMethod]
        public void RewardsComponents_NoFilter_NoClearButton() {
            var (dbuser, account) = BasicAccount();
            var built = ContractSettingsCommands.RewardsComponents(dbuser, account, 0);
            var container = (ContainerComponent)built.Components.Single();
            var labels = container.Components.OfType<ActionRowComponent>()
                .SelectMany(r => r.Components.OfType<ButtonComponent>()).Select(b => b.Label).ToList();
            CollectionAssert.DoesNotContain(labels, "Clear Filter (Do all contracts)");
        }

        [TestMethod]
        public void MainMenu_WorstCase_StaysUnder40Components() {
            // Warning + every conditional row its states allow: DM blocked, extra warning, BG on,
            // guilds on, no subscription (Ultra Pings row), active redo mode (skip-seasonal line).
            var dbuser = new DBUser {
                DiscordId = 1,
                DMSBlocked = true,
                EggIncAccounts = [new() { Id = "EI1", Group = 2, Guild = "SomeGuild" }]
            };
            var account = dbuser.EggIncAccounts[0];
            account.Assignment = new EGG9000.Common.Contracts.Assignment.AssignmentSettings {
                Redo = new EGG9000.Common.Contracts.Assignment.RedoRule { Mode = RedoLeggacyOption.YesAll }
            };
            var guild = new Guild { DisableBG = false, AllowGuilds = true };

            // Build() throws over 40 — reaching the assert proves the budget holds.
            var built = ContractSettingsCommands.MainMenu(dbuser, account, 0, guild, "extra warning");
            Assert.IsInstanceOfType<ContainerComponent>(built.Components.Single());
        }
    }
}
