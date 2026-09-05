using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Interactions;
using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord.ComponentsV2;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class ContractSettingsCommands {
        private static readonly MemoryCache _cache = new(new MemoryCacheOptions());

        public static readonly TimeSpan TimeZoneOffset = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time").GetUtcOffset(DateTimeOffset.UtcNow);
        private static readonly DateTimeOffset StaticToday = DateTimeOffset.UtcNow;
        public static readonly List<(int bg, long time)> BoardingGroupTimes = [
            (1, new DateTimeOffset(StaticToday.Year, StaticToday.Month, StaticToday.Day, 11, 0, 0 , TimeZoneOffset).ToUnixTimeSeconds()),
            (2, new DateTimeOffset(StaticToday.Year, StaticToday.Month, StaticToday.Day, 11, 0, 0 , TimeZoneOffset).AddHours(8).ToUnixTimeSeconds()),
            (3, new DateTimeOffset(StaticToday.Year, StaticToday.Month, StaticToday.Day, 11, 0, 0 , TimeZoneOffset).AddHours(16).ToUnixTimeSeconds()),
            (4, new DateTimeOffset(StaticToday.Year, StaticToday.Month, StaticToday.Day, 11, 0, 0 , TimeZoneOffset).AddHours(24).ToUnixTimeSeconds())
        ];

        public static async Task OpenContractSettings(SocketInteraction command, ApplicationDbContext db, SocketUser targetUser = null) {
            await command.DeferAsync(ephemeral: !System.Diagnostics.Debugger.IsAttached);
            var userId = targetUser?.Id ?? command.User.Id;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == userId);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ComponentsV2EmbedHelpers.Error($"Unable to locate DBUser entry for <@{userId}>"); });
            } else {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = GetAccountButtons(dbuser, "MCSMenu"); });
            }
        }

        internal static string AccountLine(EggIncAccount account) =>
            $"**Account:** {account.Backup?.UserName ?? "[unnamed]"} {account.Backup?.EarningsBonus.ToEggString()}";

        private static MessageComponent SimpleTogglePage(EggIncAccount account, int index, ulong discordId, string title, string description, string settingLabel, string settingValue, string toggleButtonLabel, string toggleCustomId) =>
            new MenuPageBuilder(title, AccountLine(account))
                .WithDescription(description)
                .AddDivider()
                .AddRow(settingLabel, settingValue, new ButtonBuilder(toggleButtonLabel, toggleCustomId, ButtonStyle.Primary))
                .WithReturn($"MCSMenu:{index},{discordId}")
                .Build();

        public static MessageComponent BGComponents(DBUser dbuser, EggIncAccount account, int index) {
            var content = "Boarding Groups (BG) set when your co-op will be launched when a contract comes out. Select which BG will allow you to be most active after a co-op is launched at that time.\n\n" +
                $"**BG times in your local timezone:**\n BG1 <t:{BoardingGroupTimes[0].time}:t> (When contracts normally launch)\n{string.Join("\n", BoardingGroupTimes.Skip(1).Where(x => x.bg != 4).Select(x => $" BG{x.bg} <t:{x.time}:t>"))}";
            return new MenuPageBuilder("Boarding Group Menu", AccountLine(account))
                .WithDescription(content)
                .AddSelect(new SelectMenuBuilder().WithCustomId($"MCSBoardingGroup:{index},{dbuser.DiscordId}").WithOptions([
                    new("Group 1 (Contract Launch)", "1", isDefault: account.Group == 1),
                    new("Group 2", "2", isDefault: account.Group == 2),
                    new("Group 3", "3", isDefault: account.Group == 3),
                ]))
                .WithReturn($"MCSMenu:{index},{dbuser.DiscordId}")
                .Build();
        }

        public static MessageComponent UBGComponents(DBUser dbuser, EggIncAccount account, int index) {
            var content = "Ultra Groups (UG) set when your co-op will be launched when an ultra contract comes out. Select which UG will allow you to be most active after a co-op is launched at that time.\n\n" +
                $"**UG times in your local timezone:**\n UG1 <t:{BoardingGroupTimes[0].time}:t> (When contracts normally launch)\n" +
                $"{string.Join("\n", BoardingGroupTimes.Skip(1).Where(x => x.bg != 4).Select(x => $" UG{x.bg} <t:{x.time}:t>"))}\n UG4 <t:{BoardingGroupTimes[3].time}:t> (24 hours after contracts launch)";
            return new MenuPageBuilder("Ultra Boarding Group Menu", AccountLine(account))
                .WithDescription(content)
                .AddSelect(new SelectMenuBuilder().WithCustomId($"MCSUBoardingGroup:{index},{dbuser.DiscordId}").WithOptions([
                    new("Ultra Group 1 (Contract Launch)", "1", isDefault: account.UltraGroup == 1),
                    new("Ultra Group 2", "2", isDefault: account.UltraGroup == 2),
                    new("Ultra Group 3", "3", isDefault: account.UltraGroup == 3),
                    new("Ultra Group 4 (24h After Contract Launch)", "4", isDefault: account.UltraGroup == 4),
                ]))
                .WithReturn($"MCSMenu:{index},{dbuser.DiscordId}")
                .Build();
        }

        // Header + description + divider + cross-flow row = 4 fixed components; each account
        // adds a 3-component row (section + text + button). Stays under MenuPageBuilder's 40-cap
        // through 11 accounts; a 12th would overflow, so guard it with a friendly message instead
        // of letting Build() throw on the flow's primary entry point.
        public const int MaxAccountsForPicker = 11;

        public static MessageComponent GetAccountButtons(DBUser dbuser, string prefix) {
            if(dbuser.EggIncAccounts.Count > MaxAccountsForPicker) {
                return ComponentsV2EmbedHelpers.Error("You have too many linked accounts to display here. Please contact staff for help managing your accounts.", "Too Many Accounts");
            }

            var page = new MenuPageBuilder("Contract Settings")
                .WithDescription("Select which account you would like to manage");
            for(var i = 0; i < dbuser.EggIncAccounts.Count; i++) {
                var account = dbuser.EggIncAccounts[i];
                page.AddRow(account.Backup?.UserName ?? "[unnamed]", account.Backup?.EarningsBonus.ToEggString() ?? "",
                    new ButtonBuilder("Manage", $"{prefix}:{i},{dbuser.DiscordId}", ButtonStyle.Primary));
            }
            page.AddDivider()
                .AddButtons(
                    new ButtonBuilder("Coop Settings", $"CSAccountMenu:{dbuser.DiscordId},true,false", ButtonStyle.Secondary),
                    new ButtonBuilder("Ship Return DM", $"SRDMenu:{dbuser.DiscordId}", ButtonStyle.Secondary));
            return page.Build();
        }

        public static MessageComponent MainMenu(DBUser dbuser, EggIncAccount account, int index, Guild dbguild, string extraWarning = null) {
            var page = new MenuPageBuilder("Contract Settings", AccountLine(account));

            if(dbuser.DMSBlocked)
                page.WithAccent(Color.Red).AddText("⚠ <@514257192803893272> is currently blocked from sending you Direct Messages (DMs.) This could either be due to Server Privacy settings, or directly blocking the bot. Please reach out to Staff for questions.");
            if(extraWarning != null)
                page.WithAccent(Color.Red).AddText($"⚠ {extraWarning}");

            page.AddDivider();
            page.AddRow("Break", MCSBreakMessage(account).Trim(), new ButtonBuilder("Set Break", $"MCSBreak:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            if(!dbguild.DisableBG) {
                page.AddDivider();
                var bgValue = account.Group != default ? $"BG{account.Group}: Co-ops start just after <t:{BoardingGroupTimes[account.Group - 1].time}:t>" : "Not Set";
                page.AddRow("Boarding Group", bgValue, new ButtonBuilder("Change", $"MCSBg:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
                if(account.HasActiveSubscription()) {
                    var ugValue = account.UltraGroup != default ? $"UG{account.UltraGroup}: Co-ops start just after <t:{BoardingGroupTimes[account.UltraGroup - 1].time}:t>" : "Not Set";
                    page.AddRow("Ultra Boarding Group", ugValue, new ButtonBuilder("Change", $"MCSUBg:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
                }
            }

            var rewardDict = GetRewardDictionary();
            account.Assignment.RewardFilter ??= [];
            var rewards = account.Assignment.RewardFilter.Any() ? string.Join(", ", account.Assignment.RewardFilter.Select(x => rewardDict[x])) : "All Contracts";
            page.AddDivider();
            page.AddRow("Rewards Filter", rewards, new ButtonBuilder("Filter", $"MCSRewards:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            var redoValue = account.Assignment.Redo.Mode switch {
                RedoLeggacyOption.YesAll => "Yes, redo all contracts to help out others",
                RedoLeggacyOption.YesNoUltra => "Yes, except completed Ultra contracts",
                RedoLeggacyOption.YesThreshold => $"Yes, if previous score under {account.Assignment.Redo.ScoreThreshold:N0}",
                RedoLeggacyOption.YesOtherAccountMatch => "Yes, if another of your accounts is assigned",
                _ => "No"
            };
            if(account.Assignment.Redo.Mode != RedoLeggacyOption.NotSet && account.Assignment.Redo.Mode != RedoLeggacyOption.No)
                redoValue += $"\nSkip Seasonal Replays: {(account.Assignment.Redo.ExcludeSeasonal ? "ON" : "OFF")}";
            var colleggtibleOn = account.Assignment.Get(PermanentRewardKind.Colleggtible).Mode == ForceMode.AssignIfMissing;

            page.AddDivider();
            page.AddRow("Seasonal Contracts", SeasonalSummary(account), new ButtonBuilder("Change", $"MCSSeasonalPe:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            page.AddRow("Redo Leggacies", redoValue, new ButtonBuilder("Configure", $"MCSRL:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            page.AddRow("2 → 3 Contracts", account.Assignment.TwoToThree ? "Yes" : "No", new ButtonBuilder("Change", $"MCSTwoToThree:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            page.AddRow("Colleggtibles", colleggtibleOn ? "Yes" : "No", new ButtonBuilder("Change", $"MCSColleggtible:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            if(!account.HasActiveSubscription() || dbguild.AllowGuilds)
                page.AddDivider();
            if(!account.HasActiveSubscription())
                page.AddRow("Ultra Offer Pings", account.PingForNCUltra ? "On" : "Off", new ButtonBuilder("Change", $"MCSUltraPing:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            if(dbguild.AllowGuilds)
                page.AddRow("Guild", string.IsNullOrWhiteSpace(account.Guild) ? "Not Set" : account.Guild.Truncate(50), new ButtonBuilder("Set Guild", $"MCSGuild:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            page.WithReturn($"MCSAccounts:{dbuser.DiscordId}");
            return page.Build();
        }

        // Single source of truth for reward labels; the shared dictionary includes EggsOfProphecy,
        // which must be present here — accounts can legitimately carry PE in their RewardFilter and
        // the main-menu render indexes this dictionary with every filter entry.
        public static Dictionary<Ei.RewardType, string> GetRewardDictionary() => ContractSettingsHelpers.GetRewardDictionary();

        public const int maxThresh = 90000;


        public static MessageComponent RedoLeggaciesComponents(DBUser dbuser, EggIncAccount account, int index) {
            var redoText = account.Assignment.Redo.Mode switch {
                RedoLeggacyOption.YesAll => "Yes (Will redo all contracts to help out others)",
                RedoLeggacyOption.YesNoUltra => "Yes (Will not redo completed Ultra contracts)",
                RedoLeggacyOption.YesThreshold => $"Yes (If previous score was under {account.Assignment.Redo.ScoreThreshold} score)",
                RedoLeggacyOption.YesOtherAccountMatch => "Yes (If any other of your accounts get assigned, also applies to seasonal contracts)",
                _ => "No (Will still be assigned to incomplete leggacies)"
            };
            var content = "This option allows you to determine which Leggacy contracts you will redo, when they are offered in-game. The \"other account matches\" option also applies to Seasonal contracts, forcing this account in whenever a sibling account is force-assigned by the seasonal filter.\n\n**NOTE:** You will **always** be assigned to incomplete Leggacy contracts, so long as they match your rewards filter.";

            var page = new MenuPageBuilder("Redo Leggacies Menu", AccountLine(account))
                .WithDescription(content)
                .AddDivider();

            if(account.Assignment.Redo.Mode == RedoLeggacyOption.YesThreshold)
                page.AddRow("Redo Completed Leggacies", redoText, new ButtonBuilder("Change CS Threshold", $"RLThreshModal:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            else
                page.AddRow("Redo Completed Leggacies", redoText);

            if(account.Assignment.Redo.Mode != RedoLeggacyOption.NotSet && account.Assignment.Redo.Mode != RedoLeggacyOption.No) {
                var skipLabel = account.Assignment.Redo.ExcludeSeasonal ? "ON" : "OFF";
                page.AddRow("Skip Seasonal Replays", $"{skipLabel} (also applies to seasonal contracts you've already completed)",
                    new ButtonBuilder($"Skip Seasonal Replays: {skipLabel}", $"MCSExcludeSeasonal:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            }

            page.AddSelect(new SelectMenuBuilder().WithCustomId($"MCSRedoLeggacies:{index},{dbuser.DiscordId}").WithOptions(GetRedoLeggacyOptions(account, dbuser)));
            page.WithReturn($"MCSMenu:{index},{dbuser.DiscordId}");
            return page.Build();
        }

        public static List<SelectMenuOptionBuilder> GetRedoLeggacyOptions(EggIncAccount account, DBUser dbuser) {
            var list = new List<SelectMenuOptionBuilder>() {
                new("Yes (Will redo all contracts to help out others)", "1", isDefault: account.Assignment.Redo.Mode == RedoLeggacyOption.YesAll),
                new($"Yes (If your previous score was under a threshold you set)", "2", isDefault: account.Assignment.Redo.Mode == RedoLeggacyOption.YesThreshold),
            };
            if(account.HasActiveSubscription()) {
                list.Add(new($"Yes (Will not redo completed Ultra contracts)", "5", isDefault: account.Assignment.Redo.Mode == RedoLeggacyOption.YesNoUltra));
            }
            if(dbuser.EggIncAccounts.Count > 1) {
                list.Add(new("Yes (If any other of your accounts get assigned)", "4", isDefault: account.Assignment.Redo.Mode == RedoLeggacyOption.YesOtherAccountMatch));
            }
            list.Add(new("No (Will still be assigned to incomplete leggacies)", "3", isDefault: account.Assignment.Redo.Mode == RedoLeggacyOption.No));
            return list;
        }

        public static string SeasonalSummary(EggIncAccount account) {
            var seasonal = account.Assignment.Seasonal ?? new SeasonalRule();
            var after = seasonal.RewardFilterAfter ? ", then reward filter" : "";
            // Show the grade-floored goal so a stored 0 / below-floor value isn't displayed as the
            // effective setting.
            var effective = seasonal.EffectiveCsGoal(account.GetGrade());
            return seasonal.Mode switch {
                SeasonalMode.UntilPeEarned => $"Until PE earned{after}",
                SeasonalMode.UntilCsGoal => $"Until CS {effective:N0} (min){after}",
                _ => "Always assign"
            };
        }

        public static MessageComponent SeasonalComponents(DBUser dbuser, EggIncAccount account, int index, double? latestSeasonPeExample = null, string adjustedNote = null) {
            var seasonal = account.Assignment.Seasonal ?? new SeasonalRule();
            var mode = seasonal.Mode;

            var content = "Force-assigns seasonal contracts you haven't completed yet (e.g. you missed a season's PE). Choose how long you should keep being force-assigned. Already-completed seasonal contracts are governed by Redo Completed Leggacies instead.";
            var note = "If your CS goal is below the season's PE goal, it will not be used - you stay assigned until you earn the season PE.";
            if(latestSeasonPeExample is > 0)
                note += $"\n\nLatest season's PE goal for grade {account.GetGrade().ToString().Replace("Grade", "")}: `{latestSeasonPeExample.Value:N0}` CS (example - varies per season).";

            var page = new MenuPageBuilder("Seasonal Contracts Menu", AccountLine(account))
                .WithDescription(content)
                .AddDivider()
                .AddRow("Current Setting", SeasonalSummary(account))
                .AddRow("Seasonal PE goal", note);

            if(adjustedNote != null)
                page.AddText($"⚠ {adjustedNote}");

            if(mode == SeasonalMode.UntilCsGoal)
                page.AddRow("CS Goal", $"{seasonal.EffectiveCsGoal(account.GetGrade()):N0}", new ButtonBuilder("Set CS Goal", $"SeasonalPeThreshModal:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            if(mode == SeasonalMode.UntilPeEarned || mode == SeasonalMode.UntilCsGoal) {
                var filterAfterLabel = seasonal.RewardFilterAfter ? "ON" : "OFF";
                page.AddRow("Reward filter after", filterAfterLabel, new ButtonBuilder($"Reward filter after: {filterAfterLabel}", $"MCSSeasonalFilterAfter:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            }

            page.AddSelect(new SelectMenuBuilder().WithCustomId($"MCSSeasonalPeSet:{index},{dbuser.DiscordId}").WithOptions([
                new("Always assign", "0", isDefault: mode == SeasonalMode.AlwaysAssign),
                new("Assign until I earn the PE", "1", isDefault: mode == SeasonalMode.UntilPeEarned),
                new("Assign until a CS goal", "2", isDefault: mode == SeasonalMode.UntilCsGoal),
            ]));
            page.WithReturn($"MCSMenu:{index},{dbuser.DiscordId}");
            return page.Build();
        }

        // PE-CS goal for the account's grade in the most recent season. 0 when no season / no PE goal.
        public static async Task<double> LatestSeasonPeExample(ApplicationDbContext db, EggIncAccount account) {
            var latest = await db.SeasonInfos.OrderByDescending(s => s.StartTime).FirstOrDefaultAsync();
            return latest?.GetMaxPeCxp(account.GetGrade()) ?? 0;
        }

        public static MessageComponent TwoToThreeComponents(DBUser dbuser, EggIncAccount account, bool enabled, int index) {
            var msg = "Ocasionally, Leggacy Contracts will be released with three rewards, despite previously having two rewards. In your contract history, this will appear as a complete contract, and auto-assignment will not happen, by default.\n" +
                "\n- If set to `No`, you will not be assigned coops for contracts in which only a new third reward is offered." +
                "\n- If set to `Yes`, you will be automatically assigned a co-op for these \"`2 -> 3`\" Leggacy Contracts.";
            return SimpleTogglePage(account, index, dbuser.DiscordId, "2 -> 3 Contract Reward Menu", msg, "Auto-Assign 2 -> 3 Contracts", enabled ? "Yes" : "No",
                enabled ? "Disable 2 -> 3 Auto-Assignments" : "Enable 2 -> 3 Auto-Assignments", $"MCSToggleTwoToThree:{index},{dbuser.DiscordId},{(enabled ? "f" : "t")}");
        }

        public static async Task<MessageComponent> ColleggtiblesComponents(ApplicationDbContext db, DBUser dbuser, EggIncAccount account, bool enabled, int index) {
            var customEggs = await db.GetCustomEggsAsync();
            var msg = $"Colleggtibles are **[Custom Eggs](<https://egg-inc.fandom.com/wiki/Colleggtibles>)** that reward permanent buffs when you achieve certain habitat populations farming a contract of that egg. " +
                $"Each Colleggtible egg has 4 levels, which all provide the same type of buff, at different efficacies. Levels unlock at:\n- Level 1: **10 Million** :chicken:\n- Level 2: **100 Million** :chicken:\n- Level 3: **1 Billion** :chicken:\n- Level 4: **10 Billion** :chicken:\n\n" +
                $"**__Your colleggtibles__**\n\n{getAccountColleggtibles(account.Backup, customEggs)}\n" +
                $"You can enable this option to be automatically assigned to all Colleggtible Contracts that you do not have at max level already.";
            return SimpleTogglePage(account, index, dbuser.DiscordId, "Colleggtibles Contract Menu", msg, "Auto-Assign Colleggtibles", enabled ? "Yes" : "No",
                enabled ? "Disable Colleggtible Auto-Assignments" : "Enable Colleggtible Auto-Assignments", $"MCSToggleColleggtible:{index},{dbuser.DiscordId},{(enabled ? "f" : "t")}");
        }

        private static string getAccountColleggtibles(CustomBackup backup, List<DBCustomEgg> customEggs) {
            var sb = new StringBuilder();
            foreach(var customEgg in customEggs) {
                var colleggtibleLevel = backup?.GetColleggtibleLevel(customEgg.Identifier) ?? 0;
                if(colleggtibleLevel == 0) {
                    sb.AppendLine($"{customEgg.Emoji} - _Not unlocked_ {GetTheoreticalModifierString(customEgg)}");
                } else {
                    sb.AppendLine($"{customEgg.Emoji} - **Level {colleggtibleLevel}: {GetModifierString(customEgg.Modifiers[(int)colleggtibleLevel - 1])}**");
                }
            }
            var unreleasedEggs = customEggs.Where(c => !c.Released);
            if(unreleasedEggs.Any()) {
                var multiple = unreleasedEggs.Count() > 1;
                var eggEmojiString = string.Join(" ", unreleasedEggs.Select(ce => ce.Emoji));
                sb.AppendLine($"\n-# \\* {eggEmojiString} Egg{(multiple ? "s have" : " has")} not been seen in a Contract yet.\n-# As such, {(multiple ? "their effects are" : "its effect is")} still subject to possible change before {(multiple ? "their" : "its")} release.");
            }
            return sb.ToString();
        }

        private static string GetTheoreticalModifierString(DBCustomEgg egg) {
            var firstMod = egg.Modifiers.First();
            return $"({firstMod.Sign()} {firstMod.DimensionName()}{(egg.Released ? "" : " \\*")})";
        }

        private static string GetModifierString(DBCustomEggModifier modifier) {
            return $"{modifier.PercentString()} {modifier.DimensionName()}";
        }

        public static MessageComponent UltraPingComponents(DBUser dbuser, EggIncAccount account, bool enabled, int index) {
            var msg = "This option allows you to be notified when a Leggacy PE <:Egg_of_Prophecy_PE:669981330477547580> Contract that you have not finished, is offered to <:ultra:1131045418319495369> Egg, Inc. Ultra players. " +
                "These pings will occur when Ultra Contracts are released, on Fridays at " +
                $"<t:{new DateTimeOffset(2023, 5, 1, 11, 0, 0, TimeSpan.FromHours(-5)).ToUnixTimeSeconds()}:t>.";
            return SimpleTogglePage(account, index, dbuser.DiscordId, "Ultra Offer Pings Menu", msg, "Ultra Offer Pings", enabled ? "Enabled" : "Disabled",
                enabled ? "Disable Pings" : "Enable Pings", $"MCSUltraPingToggle:{index},{dbuser.DiscordId},{(enabled ? "f" : "t")}");
        }

        public static MessageComponent BreakComponents(DBUser user, EggIncAccount account, int index) {
            var page = new MenuPageBuilder("Break Menu", AccountLine(account))
                .WithDescription("Setting a break will prevent you from being added to co-ops for the duration of the break.");

            if(user.GuildId == 656455567858073601 || user.GuildId == 1108127105088241746) { // Palace / Dev E9K
                page.AddText("⚠️ **NOTE**\n```This is for when you need a break from all contracts;\nYou are still expected to not do outside coops while on break.```");
            }

            page.AddRow("Break", MCSBreakMessage(account).Trim());

            var buttons = new List<ButtonBuilder>();
            if(account.OnBreakUntil < DateTime.Now.AddDays(60) || account.OnBreakUntil == default) {
                buttons.Add(new ButtonBuilder("Add 1 Day to Break", $"BreakAddDay:{index},{user.DiscordId}", ButtonStyle.Primary));
                buttons.Add(new ButtonBuilder("Add 1 Week to Break", $"BreakAddWeek:{index},{user.DiscordId}", ButtonStyle.Primary));
            }
            if(account.OnBreakUntil != default && account.OnBreakUntil > DateTimeOffset.UtcNow)
                buttons.Add(new ButtonBuilder("Stop Break Early", $"StopBreakEarly:{index},{user.DiscordId}", ButtonStyle.Danger));
            if(buttons.Count > 0)
                page.AddButtons([.. buttons]);

            return page.WithReturn($"MCSMenu:{index},{user.DiscordId}").Build();
        }

        public static string MCSBreakMessage(EggIncAccount account) {
            if(account.OnBreakUntil == default) {
                return "Not on break";
            } else if(account.OnBreakUntil < DateTimeOffset.UtcNow) {
                return $"\nBreak Ended <t:{account.OnBreakUntil.ToUnixTimeSeconds()}:R> on <t:{account.OnBreakUntil.ToUnixTimeSeconds()}:D>\n";
            } else {
                return $"\nEnds <t:{account.OnBreakUntil.ToUnixTimeSeconds()}:R> on <t:{account.OnBreakUntil.ToUnixTimeSeconds()}:D>\n";
            }
        }

        public static DateTimeOffset AddCappedDays(DateTimeOffset currentDtOffset, int daysToAdd) {
            var dayDifferential = (currentDtOffset - DateTimeOffset.UtcNow).Days;
            if(dayDifferential >= 60) return currentDtOffset;
            else {
                if(dayDifferential + daysToAdd >= 60) daysToAdd = 60 - dayDifferential;
                return currentDtOffset.AddDays(daysToAdd);
            }
        }

        public static MessageComponent RewardsComponents(DBUser dbuser, EggIncAccount account, int index) {
            account.Assignment.RewardFilter ??= [];
            var content = "If you only want to do contracts with certain rewards, please select those rewards below. You won't be automatically added to any contract that doesn't contain those rewards. If you select Clear Filter it'll set you to do all contracts regardless of rewards.";

            var select = new SelectMenuBuilder()
                .WithCustomId($"MCSRewardsSet:{index},{dbuser.DiscordId}")
                .WithPlaceholder("Rewards Filter")
                .WithMinValues(0).WithMaxValues(GetRewardDictionary().Count);
            foreach(var item in GetRewardDictionary())
                select.AddOption(item.Value, ((int)item.Key).ToString(), isDefault: account.Assignment.RewardFilter.Any(x => x == item.Key));

            var page = new MenuPageBuilder("Rewards Filter Menu", AccountLine(account))
                .WithDescription(content)
                .AddSelect(select);

            if(account.Assignment.RewardFilter.Count > 0)
                page.AddButtons(new ButtonBuilder("Clear Filter (Do all contracts)", $"MCSRewardsClear:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            return page.WithReturn($"MCSMenu:{index},{dbuser.DiscordId}").Build();
        }
    }

    public class ContractSettingsModule(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<ContractSettingsModule> logger) : E9KModuleBase(dbFactory) {
        private readonly ILogger<ContractSettingsModule> _logger = logger;

        [SlashCommand("mycontractsettings", "My Contract Settings")]
        [CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
        public async Task MyContractSettings() {
            var command = Context.Interaction;
            var db = Db;
            await command.DeferAsync(ephemeral: !System.Diagnostics.Debugger.IsAttached);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ComponentsV2EmbedHelpers.Error($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
            } else if(dbuser.GuildId == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ComponentsV2EmbedHelpers.Error($"It looks like the bot is unable to see what server you are registered with, please use the command `/moveserver` and then try this command again."); });
            } else {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.GetAccountButtons(dbuser, "MCSMenu"); });
            }
        }

        [ComponentInteraction("MCSAccounts:*", ignoreGroupNames: true)]
        public async Task MCSAccounts(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            if(!component.HasResponded) await component.DeferAsync();
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.GetAccountButtons(dbuser, "MCSMenu"); });
        }

        [ComponentInteraction("MCSMenu:*", ignoreGroupNames: true)]
        public async Task MCSMenu(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            if(!component.HasResponded) await component.DeferAsync();
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var components = ContractSettingsCommands.MainMenu(dbuser, account, index, Db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("MCSBg:*", ignoreGroupNames: true)]
        public async Task MCSBg(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.BGComponents(dbuser, account, index); });
        }

        [ComponentInteraction("MCSBoardingGroup:*", ignoreGroupNames: true)]
        public async Task MCSBoardingGroup(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.Group = byte.Parse(component.Data.Values.First());
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            var components = ContractSettingsCommands.MainMenu(dbuser, dbuser.EggIncAccounts[index], index, Db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("MCSUBg:*", ignoreGroupNames: true)]
        public async Task MCSUBg(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.UBGComponents(dbuser, account, index); });
        }

        [ComponentInteraction("MCSUBoardingGroup:*", ignoreGroupNames: true)]
        public async Task MCSUBoardingGroup(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.UltraGroup = byte.Parse(component.Data.Values.First());
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            var components = ContractSettingsCommands.MainMenu(dbuser, dbuser.EggIncAccounts[index], index, Db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("MCSRL:*", ignoreGroupNames: true)]
        public async Task MCSRL(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.RedoLeggaciesComponents(dbuser, account, index); });
        }

        [ComponentInteraction("RLThreshModal:*", ignoreGroupNames: true)]
        public async Task RLThreshModal(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            var modal = new ModalBuilder().WithTitleSafe("Update CS Threshold").WithCustomId($"RlThreshUpdate:{index},{dbuser.DiscordId}")
                .AddTextInputSafe(label: $"Enter CS Threshold between 0 and {ContractSettingsCommands.maxThresh}", value: account.Assignment.Redo.ScoreThreshold.ToString(), customId: "num", required: true).Build();

            await component.RespondWithModalAsync(modal);
        }

        [ModalInteraction("RlThreshUpdate:*", ignoreGroupNames: true)]
        public async Task RlThreshUpdate(string data, NumberInputModal form) {
            var modal = (SocketModal)Context.Interaction;
            var numText = form.Num?.ToLower();
            //Parse to double so that we can handle things like "25.2k"
            var isNum = double.TryParse((numText.Last() == 'k' ? numText.Remove(numText.Length - 1) : numText), out var num);
            if(isNum && (numText.Last() == 'k')) num *= 1000;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));
            var index = int.Parse(data.Split(",")[0]);

            if(!isNum || (num <= 0 || num > ContractSettingsCommands.maxThresh)) {
                var errMsg = $"⚠️ `{numText}` not accepted - Input must be " + (!isNum ? "a number" : (num <= 0 ? "a positive integer" : $"less than `{ContractSettingsCommands.maxThresh:n0}`"));
                var components = ComponentsV2EmbedHelpers.ErrorWithRetry("Redo Leggacies Menu", errMsg, $"RLThreshModal:{index},{dbuser.DiscordId}", $"MCSRL:{index},{dbuser.DiscordId}");
                await modal.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
            } else {
                var account = dbuser.EggIncAccounts[index];
                account.Assignment.Redo.ScoreThreshold = (int)num;
                dbuser.UpdateAccounts();
                await Db.SaveChangesAsync();
                await modal.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.RedoLeggaciesComponents(dbuser, account, index); });
            }
        }

        [ComponentInteraction("MCSRedoLeggacies:*", ignoreGroupNames: true)]
        public async Task MCSRedoLeggacies(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.Assignment.Redo.Mode = (RedoLeggacyOption)Enum.Parse(typeof(RedoLeggacyOption), component.Data.Values.First());
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();

            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.RedoLeggaciesComponents(dbuser, account, index); });
        }

        [ComponentInteraction("MCSExcludeSeasonal:*", ignoreGroupNames: true)]
        public async Task MCSExcludeSeasonal(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.Assignment.Redo.ExcludeSeasonal = !account.Assignment.Redo.ExcludeSeasonal;
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.RedoLeggaciesComponents(dbuser, account, index); });
        }

        [ComponentInteraction("MCSSeasonalPe:*", ignoreGroupNames: true)]
        public async Task MCSSeasonalPe(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var peExample = await ContractSettingsCommands.LatestSeasonPeExample(Db, account);
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.SeasonalComponents(dbuser, account, index, peExample); });
        }

        [ComponentInteraction("MCSSeasonalPeSet:*", ignoreGroupNames: true)]
        public async Task MCSSeasonalPeSet(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.Assignment.Seasonal ??= new SeasonalRule();
            account.Assignment.Seasonal.Mode = (SeasonalMode)int.Parse(component.Data.Values.First());
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            var peExample = await ContractSettingsCommands.LatestSeasonPeExample(Db, account);
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.SeasonalComponents(dbuser, account, index, peExample); });
        }

        [ComponentInteraction("MCSSeasonalFilterAfter:*", ignoreGroupNames: true)]
        public async Task MCSSeasonalFilterAfter(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.Assignment.Seasonal ??= new SeasonalRule();
            account.Assignment.Seasonal.RewardFilterAfter = !account.Assignment.Seasonal.RewardFilterAfter;
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            var peExample = await ContractSettingsCommands.LatestSeasonPeExample(Db, account);
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.SeasonalComponents(dbuser, account, index, peExample); });
        }

        [ComponentInteraction("SeasonalPeThreshModal:*", ignoreGroupNames: true)]
        public async Task SeasonalPeThreshModal(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            var modal = new ModalBuilder()
                .WithTitleSafe("Set Seasonal CS Goal")
                .WithCustomId($"SeasonalPeThreshUpdate:{index},{dbuser.DiscordId}")
                .AddTextInputSafe(
                    label: "Assign until contract score reaches",
                    value: (account.Assignment.Seasonal ?? new SeasonalRule()).EffectiveCsGoal(account.GetGrade()).ToString("N0"),
                    customId: "num",
                    required: true)
                .Build();

            await component.RespondWithModalAsync(modal);
        }

        [ModalInteraction("SeasonalPeThreshUpdate:*", ignoreGroupNames: true)]
        public async Task SeasonalPeThreshUpdate(string data, NumberInputModal form) {
            var modal = (SocketModal)Context.Interaction;
            var numText = form.Num?.ToLower().Replace(",", "");
            var isNum = double.TryParse(
                numText.EndsWith("k") ? numText[..^1] : numText,
                out var num);
            if(isNum && numText.EndsWith("k")) num *= 1000;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            var floor = SeasonalRule.CsGoalFloor(account.GetGrade());
            var peExample = await ContractSettingsCommands.LatestSeasonPeExample(Db, account);

            if(!isNum || num < 0) {
                var errMsg = $"⚠️ `{numText}` not accepted - enter a number (e.g. `{floor:N0}` or `{(floor / 1000):N0}k`)";
                var components = ComponentsV2EmbedHelpers.ErrorWithRetry("Seasonal Contracts Menu", errMsg, $"SeasonalPeThreshModal:{index},{dbuser.DiscordId}", $"MCSSeasonalPe:{index},{dbuser.DiscordId}");
                await modal.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
            } else {
                // Anti-dodge: clamp to the grade floor so the seasonal force doesn't clear on first run.
                var clamped = Math.Max(num, floor);
                account.Assignment.Seasonal ??= new SeasonalRule();
                account.Assignment.Seasonal.CsGoal = clamped;
                dbuser.UpdateAccounts();
                await Db.SaveChangesAsync();
                var adjustedNote = clamped > num
                    ? $"Minimum CS goal for grade {account.GetGrade().ToString().Replace("Grade", "")} is `{floor:N0}`. Set to `{clamped:N0}`."
                    : null;
                var components = ContractSettingsCommands.SeasonalComponents(dbuser, account, index, peExample, adjustedNote);
                await modal.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
            }
        }

        [ComponentInteraction("MCSTwoToThree:*", ignoreGroupNames: true)]
        public async Task MCSTwoToThree(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.TwoToThreeComponents(dbuser, account, account.Assignment.TwoToThree, index); });
        }

        [ComponentInteraction("MCSToggleTwoToThree:*", ignoreGroupNames: true)]
        public async Task MCSToggleTwoToThree(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var toggleState = data.Split(",")[2] == "t";

            account.Assignment.TwoToThree = toggleState;
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();

            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.TwoToThreeComponents(dbuser, account, toggleState, index); });
        }

        [ComponentInteraction("MCSColleggtible:*", ignoreGroupNames: true)]
        public async Task MCSColleggtible(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var enabled = account.Assignment.Get(PermanentRewardKind.Colleggtible).Mode == ForceMode.AssignIfMissing;
            var components = await ContractSettingsCommands.ColleggtiblesComponents(Db, dbuser, account, enabled, index);
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("MCSToggleColleggtible:*", ignoreGroupNames: true)]
        public async Task MCSToggleColleggtible(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var toggleState = data.Split(",")[2] == "t";

            account.Assignment.SetForce(PermanentRewardKind.Colleggtible, toggleState ? ForceMode.AssignIfMissing : ForceMode.NotSet);
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();

            var components = await ContractSettingsCommands.ColleggtiblesComponents(Db, dbuser, account, toggleState, index);
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("MCSUltraPing:*", ignoreGroupNames: true)]
        public async Task MCSUltraPing(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.UltraPingComponents(dbuser, account, account.PingForNCUltra, index); });
        }

        [ComponentInteraction("MCSUltraPingToggle:*", ignoreGroupNames: true)]
        public async Task MCSUltraPingToggle(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var toggleState = data.Split(",")[2] == "t";

            account.PingForNCUltra = toggleState;
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();

            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.UltraPingComponents(dbuser, account, toggleState, index); });
        }

        [ComponentInteraction("MCSBreak:*", ignoreGroupNames: true)]
        public async Task MCSBreak(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.BreakComponents(dbuser, account, index); });
        }

        [ComponentInteraction("BreakAddDay:*", ignoreGroupNames: true)]
        public async Task BreakAddDay(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.SetBreak(ContractSettingsCommands.AddCappedDays(account.OnBreakUntil == default || account.OnBreakUntil < DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : account.OnBreakUntil, 1), dbuser);
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.BreakComponents(dbuser, account, index); });
        }

        [ComponentInteraction("BreakAddWeek:*", ignoreGroupNames: true)]
        public async Task BreakAddWeek(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.SetBreak(ContractSettingsCommands.AddCappedDays(account.OnBreakUntil == default || account.OnBreakUntil < DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : account.OnBreakUntil, 7), dbuser);
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.BreakComponents(dbuser, account, index); });
        }

        [ComponentInteraction("StopBreakEarly:*", ignoreGroupNames: true)]
        public async Task StopBreakEarly(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.SetBreak(default, dbuser);
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.BreakComponents(dbuser, account, index); });
        }

        [ComponentInteraction("MCSRewards:*", ignoreGroupNames: true)]
        public async Task MCSRewards(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = ContractSettingsCommands.RewardsComponents(dbuser, account, index); });
        }

        [ComponentInteraction("MCSRewardsSet:*", ignoreGroupNames: true)]
        public async Task MCSRewardsSet(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var reg = dbuser.EggIncAccounts[index];

            reg.Assignment.RewardFilter = [.. component.Data.Values.Select(x => (Ei.RewardType)Enum.Parse(typeof(Ei.RewardType), x))];
            if(reg.Assignment.RewardFilter.Any(x => x == Ei.RewardType.UnknownReward)) {
                reg.Assignment.RewardFilter = [];
            }
            _logger.LogInformation("{user}'s rewards updated to {list}", dbuser.DiscordUsername, string.Join(",", reg.Assignment.RewardFilter.Select(r => r.ToString())));
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            var components = ContractSettingsCommands.MainMenu(dbuser, dbuser.EggIncAccounts[index], index, Db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("MCSRewardsClear:*", ignoreGroupNames: true)]
        public async Task MCSRewardsClear(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var reg = dbuser.EggIncAccounts[index];
            reg.Assignment.RewardFilter = [];
            dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            var components = ContractSettingsCommands.MainMenu(dbuser, dbuser.EggIncAccounts[index], index, Db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }

        [ComponentInteraction("MCSGuild:*", ignoreGroupNames: true)]
        public async Task MCSGuild(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            var modal = new ModalBuilder().WithTitleSafe("Enter Guild Name (leave blank for none)").WithCustomId($"MCSGuildUpdate:{index},{dbuser.DiscordId}")
                .AddTextInputSafe(label: $"Enter Guild Name (leave blank for none)", value: account.Guild, customId: "name", required: false).Build();

            await component.RespondWithModalAsync(modal);
        }

        [ModalInteraction("MCSGuildUpdate:*", ignoreGroupNames: true)]
        public async Task MCSGuildUpdate(string data, GuildNameModal form) {
            var modal = (SocketModal)Context.Interaction;
            var name = form.Name;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));
            var index = int.Parse(data.Split(",")[0]);

            var account = dbuser.EggIncAccounts[index];
            var guildNameDifferent = account.Guild != name.Truncate(100);
            account.Guild = name.Truncate(100);
            var changed = dbuser.UpdateAccounts();
            await Db.SaveChangesAsync();
            var noChangeWarning = (guildNameDifferent && !changed)
                ? "No changes were made but were supposed to, please try again. (Kendrome is attempting to figure out why this is happening to fix it)"
                : null;
            var components = ContractSettingsCommands.MainMenu(dbuser, account, index, Db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId), noChangeWarning);
            await modal.UpdateAsync(x => { x.Content = ""; x.Embed = null; x.Embeds = null; x.Flags = MessageFlags.ComponentsV2; x.Components = components; });
        }
    }

    public class GuildNameModal : IModal {
        public string Title => "Enter Guild Name";

        [InputLabel("Enter Guild Name (leave blank for none)")]
        [ModalTextInput("name")]
        [RequiredInput(false)]
        public string Name { get; set; }
    }

    public class NumberInputModal : IModal {
        public string Title => "Enter a Number";

        [InputLabel("Number")]
        [ModalTextInput("num")]
        [RequiredInput(true)]
        public string Num { get; set; }
    }

    public partial class AdminModule {
        [SlashCommand("contractsettings", "Set another user's settings")]
        [StaffOnly(StaffTier.FarmHand)]
        public async Task ContractSettings([Summary("user")] SocketUser user) {
            await ContractSettingsCommands.OpenContractSettings(Context.Interaction, Db, user);
        }
    }
}
