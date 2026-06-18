using Discord;
using Discord.WebSocket;

using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class ContractSettingsCommands {
        private static readonly MemoryCache _cache = new(new MemoryCacheOptions());

        private static EmbedBuilder MenuEmbedTemplate(string title, string description, EggIncAccount account, DBUser dbuser) {
            var userText = dbuser.EggIncAccounts.Count > 1 ? $"For Account {account.Backup?.UserName ?? "[unnamed]"} {account.Backup?.EarningsBonus.ToEggString()}\n\n" : "";
            return new EmbedBuilder().WithTitle(title).WithDescription(userText + description);
        }

        public static readonly TimeSpan TimeZoneOffset = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time").GetUtcOffset(DateTimeOffset.UtcNow);
        private static readonly DateTimeOffset StaticToday = DateTimeOffset.UtcNow;
        public static readonly List<(int bg, long time)> BoardingGroupTimes = [
            (1, new DateTimeOffset(StaticToday.Year, StaticToday.Month, StaticToday.Day, 11, 0, 0 , TimeZoneOffset).ToUnixTimeSeconds()),
            (2, new DateTimeOffset(StaticToday.Year, StaticToday.Month, StaticToday.Day, 11, 0, 0 , TimeZoneOffset).AddHours(8).ToUnixTimeSeconds()),
            (3, new DateTimeOffset(StaticToday.Year, StaticToday.Month, StaticToday.Day, 11, 0, 0 , TimeZoneOffset).AddHours(16).ToUnixTimeSeconds()),
            (4, new DateTimeOffset(StaticToday.Year, StaticToday.Month, StaticToday.Day, 11, 0, 0 , TimeZoneOffset).AddHours(24).ToUnixTimeSeconds())
        ];

        #region AdminBypass
        [SlashCommand(Description = "Set another user's settings", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task ContractSettings(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketUser user) {
            await command.DeferAsync(ephemeral: !System.Diagnostics.Debugger.IsAttached);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Embed = EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"); });
            } else {
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Select which account you would like to manage"; x.Components = GetAccountButtons(dbuser, "MCSMenu"); });
            }
        }
        #endregion

        #region MainMenu
        [SlashCommand(Description = "My Contract Settings", AllowInDMs = true)]
        public static async Task MyContractSettings(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync(ephemeral: !System.Diagnostics.Debugger.IsAttached);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x =>  x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"));
            } else if(dbuser.GuildId == 0) {
                await command.ModifyOriginalResponseAsync(x => x.Embed = EmbedError($"It looks like the bot is unable to see what server you are registered with, please use the command `/moveserver` and then try this command again."));
            } else {
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Select which account you would like to manage"; x.Components = GetAccountButtons(dbuser, "MCSMenu"); });
            }
        }

        [ComponentCommand]
        public static async Task MCSAccounts(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            if(!component.HasResponded) await component.DeferAsync();
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[0]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Components = GetAccountButtons(dbuser, "MCSMenu"); x.Embed = null; });
        }

        public static MessageComponent GetAccountButtons(DBUser dbuser, string prefix) {
            var builder = new ComponentBuilder();
            for(var i = 0; i < dbuser.EggIncAccounts.Count; i++) {
                var account = dbuser.EggIncAccounts[i];
                builder.WithButton($"Manage {account.Backup?.UserName ?? "[unnamed]"} {account.Backup?.EarningsBonus.ToEggString()}", $"{prefix}:{i},{dbuser.DiscordId}");
            }

            builder.WithButton("Coop Settings", $"CSAccountMenu:{dbuser.DiscordId},true,false");
            builder.WithButton("Ship Return DM", $"SRDMenu:{dbuser.DiscordId}");
            return builder.Build();
        }
        public static MessageProperties MainMenu(DBUser dbuser, EggIncAccount account, int index, Guild dbguild) {
            var props = new MessageProperties();

            var desc = dbuser.DMSBlocked ? "⚠ <@514257192803893272> is currently blocked from sending you Direct Messages (DMs.) This could either be due to Server Privacy settings, or directly blocking the bot. Please reach out to Staff for questions." : "";


            var eBuilder = MenuEmbedTemplate("Main Menu", desc, account, dbuser);
            if(desc != "") eBuilder.WithColor(Color.Red);

            eBuilder.AddField("Break (60 Day Max)", MCSBreakMessage(account));

            var buttons = new List<(string, string, ButtonStyle)>();

            if(!dbguild.DisableBG) {
                eBuilder.AddField("Boarding Group", account.Group != default ? $"BG{account.Group} Co-ops start just after <t:{BoardingGroupTimes.First(x => x.bg == account.Group).time}:t>" : "Not Set (please select below)");
                buttons.Add(("Boarding Group", $"MCSBg:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
                if(account.HasActiveSubscription()) {
                    eBuilder.AddField("Ultra Boarding Group", account.UltraGroup != default ? $"UG{account.UltraGroup} Co-ops start just after <t:{BoardingGroupTimes.First(x => x.bg == account.UltraGroup).time}:t>" : "Not Set (please select below)");
                    buttons.Add(("Ultra Boarding Group", $"MCSUBg:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
                }
                buttons.Add(("Rewards Filter", $"MCSRewards:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
                buttons.Add(("Leggacy Rewards Filter", $"MCSLeggacyRewards:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

                var rDict = GetRewardDictionary();
                account.AutoRegisterRewards ??= [];
                eBuilder.AddField("Rewards Filter", account.AutoRegisterRewards.Any() ? string.Join(",", account.AutoRegisterRewards.Select(x => rDict[x])) : "All Contracts");

                account.LeggacyAutoRegisterRewards ??= [];
                if(account.LeggacyAutoRegisterRewards.Any()) {
                    eBuilder.AddField("Leggacy Rewards Filter", string.Join(",", account.LeggacyAutoRegisterRewards.Select(x => rDict[x])));
                }

                eBuilder.AddField("Seasonal PE", GetSeasonalPeText(account));
                buttons.Add(("Seasonal PE", $"MCSSeasonalPe:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            }

            if(!account.HasActiveSubscription()) {
                eBuilder.AddField("Ultra Offer Pings", account.PingForNCUltra ? "Enabled" : "Disabled");
                buttons.Add(("Ultra Offer Pings", $"MCSUltraPing:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            }

            var redoText = account.RedoLeggacySelection switch {
                RedoLeggacyOption.YesAll => "Yes (Will redo all contracts to help out others)",
                RedoLeggacyOption.YesNoUltra => "Yes (Will not redo completed Ultra contracts)",
                RedoLeggacyOption.YesThreshold => $"Yes (If previous score was under {account.RedoScoreThreshold} score)",
                RedoLeggacyOption.YesOtherAccountMatch => "Yes (If any other of your accounts get assigned)",
                RedoLeggacyOption.No => "No (Will still be assigned to incomplete leggacies)",
                _ => "No (Will still be assigned to incomplete leggacies)"
            };
            eBuilder.AddField("Redo Completed Leggacies", redoText);
            buttons.Add(("Redo Completed Leggacies", $"MCSRL:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            eBuilder.AddField("Auto-Assign 2 -> 3 Contracts", account.DoTwoToThreeContracts ? "Yes" : "No");
            buttons.Add(("2 -> 3 Setting", $"MCSTwoToThree:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            eBuilder.AddField("Auto-Assign Colleggtibles", account.DoUnfinishedCollegtibles ? "Yes" : "No");
            buttons.Add(("Colleggtibles Setting", $"MCSColleggtible:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            buttons.Add(("Set Break", $"MCSBreak:{index},{dbuser.DiscordId}", ButtonStyle.Primary));

            if(dbguild.AllowGuilds) {
                eBuilder.AddField("Guild", string.IsNullOrWhiteSpace(account.Guild) ? "Not Set" : account.Guild.Truncate(100));
                buttons.Add(("Set Guild", $"MCSGuild:{index},{dbuser.DiscordId}", ButtonStyle.Primary));
            }

            buttons.Add(("Return", $"MCSAccounts:{dbuser.DiscordId}", ButtonStyle.Secondary));

            var builder = new ComponentBuilder();
            buttons.Chunk(5).ToList().ForEach(x => {
                var row = new ActionRowBuilder();
                x.ToList().ForEach(y => row.WithButton(y.Item1, y.Item2, y.Item3).Build());
                builder.AddRow(row);
            });

            props.Components = builder.Build();
            props.Embed = eBuilder.Build();

            return props;
        }

        public static Dictionary<Ei.RewardType, string> GetRewardDictionary() {
            return new Dictionary<Ei.RewardType, string> {
                { Ei.RewardType.EggsOfProphecy, "Eggs Of Prophecy" },
                { Ei.RewardType.Artifact, "Artifacts" },
                { Ei.RewardType.PiggyMultiplier, "Piggy Bank" },
                { Ei.RewardType.ShellScript, "Shell Tickets" },
                { Ei.RewardType.Gold, "Golden Eggs" },
                { Ei.RewardType.Boost, "Any Boost" },
                { Ei.RewardType.EpicResearchItem, "Epic Research" },
                { Ei.RewardType.UnknownReward, "** Any Reward **" },
            };
        }

        [ComponentCommand]
        public static async Task MCSMenu(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            if(!component.HasResponded) await component.DeferAsync();
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.ModifyOriginalResponseAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        #endregion

        #region Boarding Group
        [ComponentCommand]
        public static async Task MCSBg(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var builder = new ComponentBuilder().WithSelectMenu($"MCSBoardingGroup:{index},{dbuser.DiscordId}", [
                new("Group 1 (Contract Launch)", "1", isDefault: account.Group == 1),
                new("Group 2", "2", isDefault: account.Group == 2),
                new("Group 3", "3", isDefault: account.Group == 3),
            ]);
            builder.WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}");

            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Embed = BGEmbed(dbuser, account); });
        }

        private static Embed BGEmbed(DBUser dbuser, EggIncAccount account) {
            var content = $"Boarding Groups (BG) set when your co-op will be launched when a contract comes out.Select which BG will allow you to be most active after a co-op is launched at that time.\n\n" +
                $"Here are BG times in your local timezone:\n BG1 <t:{BoardingGroupTimes[0].time}:t>  (When contracts normally launch)\n{string.Join("\n", BoardingGroupTimes.Skip(1).Where(x => x.bg != 4).ToList().Select(x => $" BG{x.bg} <t:{x.time}:t>"))}";
            return MenuEmbedTemplate("Boarding Group Menu", content, account, dbuser).Build();
        }

        [ComponentCommand]
        public static async Task MCSBoardingGroup(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.Group = byte.Parse(component.Data.Values.First());
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region Ultra Boarding Group
        [ComponentCommand]
        public static async Task MCSUBg(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var builder = new ComponentBuilder().WithSelectMenu($"MCSUBoardingGroup:{index},{dbuser.DiscordId}", [
                new("Ultra Group 1 (Contract Launch)", "1", isDefault: account.UltraGroup == 1),
                new("Ultra Group 2", "2", isDefault: account.UltraGroup == 2),
                new("Ultra Group 3", "3", isDefault: account.UltraGroup == 3),
                new("Ultra Group 4 (24h After Contract Launch)", "4", isDefault: account.UltraGroup == 4),
            ]);
            builder.WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}");
            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Embed = UGEmbed(dbuser, account); });
        }

        private static Embed UGEmbed(DBUser dbuser, EggIncAccount account) {
            var content = $"Ultra Groups (UG) set when your co-op will be launched when an ultra contract comes out. Select which UG will allow you to be most active after a co-op is launched at that time.\n\n" +
                $"Here are UG times in your local timezone:\n UG1 <t:{BoardingGroupTimes[0].time}:t>  (When contracts normally launch)\n" +
                $"{string.Join("\n", BoardingGroupTimes.Skip(1).Where(x => x.bg != 4).ToList().Select(x => $" UG{x.bg} <t:{x.time}:t>"))}\n UG4 <t:{BoardingGroupTimes[3].time}:t>  (24 hours after contracts launch)";
            return MenuEmbedTemplate("Ultra Boarding Group Menu", content, account, dbuser).Build();
        }

        [ComponentCommand]
        public static async Task MCSUBoardingGroup(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 1 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.UltraGroup = byte.Parse(component.Data.Values.First());
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region RedoLeggacies

        //Max threshold value
        private const int maxThresh = 90000;

        [ComponentCommand]
        public static async Task MCSRL(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            await component.UpdateAsync(x => { x.Components = GetRlButtons(index, account, dbuser); x.Embed = RedoLeggaciesEmbedBuilder(dbuser, account).Build(); });
        }

        private static EmbedBuilder RedoLeggaciesEmbedBuilder(DBUser dbuser, EggIncAccount account) {
            var redoText = account.RedoLeggacySelection switch {
                RedoLeggacyOption.YesAll => "Yes (Will redo all contracts to help out others)",
                RedoLeggacyOption.YesNoUltra => "Yes (Will not redo completed Ultra contracts)",
                RedoLeggacyOption.YesThreshold => $"Yes (If previous score was under {account.RedoScoreThreshold} score)",
                RedoLeggacyOption.YesOtherAccountMatch => "Yes (If any other of your accounts get assigned)",
                RedoLeggacyOption.No => "No (Will still be assigned to incomplete leggacies)",
                _ => "No (Will still be assigned to incomplete leggacies)"
            };
            var content = "This option allows you to determine which Leggacy contracts you will redo, when they are offered in-game.\n\n**NOTE:** You will **always** be assigned to incomplete Leggacy contracts, so long as they match your rewards filter.";
            return MenuEmbedTemplate("Redo Leggacies Menu", content, account, dbuser).AddField("Redo Completed Leggacies", redoText);
        }

        private static List<SelectMenuOptionBuilder> GetRedoLeggacyOptions(EggIncAccount account, DBUser dbuser) {
            var list = new List<SelectMenuOptionBuilder>() {
                new("Yes (Will redo all contracts to help out others)", "1", isDefault: account.RedoLeggacySelection == RedoLeggacyOption.YesAll),
                new($"Yes (If your previous score was under a threshold you set)", "2", isDefault: account.RedoLeggacySelection == RedoLeggacyOption.YesThreshold),
            };
            if(account.HasActiveSubscription()) {
                list.Add(new($"Yes (Will not redo completed Ultra contracts)", "5", isDefault: account.RedoLeggacySelection == RedoLeggacyOption.YesNoUltra));
            }
            if(dbuser.EggIncAccounts.Count > 1) {
                list.Add(new("Yes (If any other of your accounts get assigned)", "4", isDefault: account.RedoLeggacySelection == RedoLeggacyOption.YesOtherAccountMatch));
            }
            list.Add(new("No (Will still be assigned to incomplete leggacies)", "3", isDefault: account.RedoLeggacySelection == RedoLeggacyOption.No));
            return list;
        }

        private static MessageComponent GetRlButtons(int index, EggIncAccount account, DBUser dbuser) {
            var builder = new ComponentBuilder().WithSelectMenu($"MCSRedoLeggacies:{index},{dbuser.DiscordId}", GetRedoLeggacyOptions(account, dbuser));

            if(account.RedoLeggacySelection == RedoLeggacyOption.YesThreshold) {
                builder.WithButton("Change CS Threshold", $"RLThreshModal:{index},{dbuser.DiscordId}");
            }

            builder.WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}", ButtonStyle.Secondary);
            return builder.Build();
        }

        [ComponentCommand]
        public static async Task RLThreshModal(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            var modal = new ModalBuilder().WithTitle("Update CS Threshold").WithCustomId($"RlThreshUpdate:{index},{dbuser.DiscordId}")
                .AddTextInput(label: $"Enter CS Threshold between 0 and {maxThresh}", value: account.RedoScoreThreshold.ToString(), customId: "num", required: true).Build();

            await component.RespondWithModalAsync(modal);
        }

        [Modal]
        public static async Task RlThreshUpdate(SocketModal modal, [ComponentData] string data, ApplicationDbContext db) {
            var numText = modal.Data.Components.First(x => x.CustomId == "num").Value.ToLower();
            //Parse to double so that we can handle things like "25.2k"
            var isNum = double.TryParse((numText.Last() == 'k' ? numText.Remove(numText.Length - 1) : numText), out var num);
            //If there was a k, multiply by 1000
            if(isNum && (numText.Last() == 'k')) num *= 1000;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));
            var index = int.Parse(data.Split(",")[0]);

            if(!isNum || (num <= 0 || num > maxThresh)) {
                var errMsg = $"⚠️ `{numText}` not accepted - Input must be " + (!isNum ? "a number" : (num <= 0 ? "a positive integer" : $"less than `{maxThresh:n0}`"));
                var embed = RedoLeggaciesEmbedBuilder(dbuser, dbuser.EggIncAccounts[index]).AddField("ERROR", errMsg).WithColor(Color.Red).Build();
                var components = new ComponentBuilder().WithButton("Re-enter", $"RLThreshModal:{index},{dbuser.DiscordId}").WithButton("Cancel", $"MCSRL:{index},{dbuser.DiscordId}").Build();
                await modal.UpdateAsync(x => { x.Content = null; x.Components = components; x.Embed = embed; });
            } else {
                var account = dbuser.EggIncAccounts[index];
                account.RedoScoreThreshold = (int)num;
                dbuser.UpdateAccounts();
                await db.SaveChangesAsync();

                var mainMenu = MainMenu(dbuser, account, index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
                await modal.UpdateAsync(x => { x.Components = GetRlButtons(index, account, dbuser); x.Embed = RedoLeggaciesEmbedBuilder(dbuser, account).Build(); });
            }
        }

        [ComponentCommand]
        public static async Task MCSRedoLeggacies(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.RedoLeggacySelection = (RedoLeggacyOption)Enum.Parse(typeof(RedoLeggacyOption), component.Data.Values.First());
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));

            await component.UpdateAsync(x => { x.Components = GetRlButtons(index, account, dbuser); x.Embed = RedoLeggaciesEmbedBuilder(dbuser, account).Build(); });
        }
        #endregion

        #region SeasonalPe

        [ComponentCommand]
        public static async Task MCSSeasonalPe(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            await component.UpdateAsync(x => { x.Components = GetSeasonalPeComponents(index, account, dbuser); x.Embed = SeasonalPeEmbed(dbuser, account).Build(); });
        }

        private static string GetSeasonalPeText(EggIncAccount account) => account.SeasonalPeOption switch {
            SeasonalPeOption.AlwaysAssignIfMissing => "Yes - assign while PE is missing",
            SeasonalPeOption.AssignIfBelowThreshold => $"Yes - assign when contract score is under {account.SeasonalPeThreshold:N0}",
            SeasonalPeOption.DontAssign => "No - skip seasonal contracts",
            _ => "Not set (assigned normally)"
        };

        private static EmbedBuilder SeasonalPeEmbed(DBUser dbuser, EggIncAccount account) {
            var content = "Seasonal PE Filter Text"; // UI: Daveed (text for the seasonal pe filter embed) 
            return MenuEmbedTemplate("Seasonal PE Menu", content, account, dbuser).AddField("Current Setting", GetSeasonalPeText(account));
        }

        private static MessageComponent GetSeasonalPeComponents(int index, EggIncAccount account, DBUser dbuser) {
            var builder = new ComponentBuilder().WithSelectMenu($"MCSSeasonalPeSet:{index},{dbuser.DiscordId}", [
                new("Not set (assigned normally)", "0", isDefault: account.SeasonalPeOption == SeasonalPeOption.NotSet),
                new("Yes - always assign while PE is missing", "1", isDefault: account.SeasonalPeOption == SeasonalPeOption.AlwaysAssignIfMissing),
                new("Yes - assign when my CXP is below a threshold", "2", isDefault: account.SeasonalPeOption == SeasonalPeOption.AssignIfBelowThreshold),
                new("No - skip seasonal contracts even if missing PE", "3", isDefault: account.SeasonalPeOption == SeasonalPeOption.DontAssign),
            ]);

            if (account.SeasonalPeOption == SeasonalPeOption.AssignIfBelowThreshold) {
                builder.WithButton("Change CXP Threshold", $"SeasonalPeThreshModal:{index},{dbuser.DiscordId}");
            }

            builder.WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}", ButtonStyle.Secondary);
            return builder.Build();
        }

        [ComponentCommand]
        public static async Task MCSSeasonalPeSet(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            account.SeasonalPeOption = (SeasonalPeOption)int.Parse(component.Data.Values.First());
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            await component.UpdateAsync(x => { x.Components = GetSeasonalPeComponents(index, account, dbuser); x.Embed = SeasonalPeEmbed(dbuser, account).Build(); });
        }

        [ComponentCommand]
        public static async Task SeasonalPeThreshModal(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            var modal = new ModalBuilder()
                .WithTitle("Set Seasonal CXP Threshold")
                .WithCustomId($"SeasonalPeThreshUpdate:{index},{dbuser.DiscordId}")
                .AddTextInput(
                    label: "Assign me when my contract score is below this value",
                    value: account.SeasonalPeThreshold.ToString("N0"),
                    customId: "num",
                    required: true)
                .Build();

            await component.RespondWithModalAsync(modal);
        }

        [Modal]
        public static async Task SeasonalPeThreshUpdate(SocketModal modal, [ComponentData] string data, ApplicationDbContext db) {
            var numText = modal.Data.Components.First(x => x.CustomId == "num").Value.ToLower().Replace(",", "");
            var isNum = double.TryParse(
                numText.EndsWith("k") ? numText[..^1] : numText,
                out var num);
            if (isNum && numText.EndsWith("k")) num *= 1000;

            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            if (!isNum || num <= 0) {
                var errMsg = $"⚠️ `{numText}` not accepted - enter a positive number (e.g. `5000` or `5k`)";
                var embed = SeasonalPeEmbed(dbuser, account).AddField("ERROR", errMsg).WithColor(Color.Red).Build();
                var components = new ComponentBuilder()
                    .WithButton("Re-enter", $"SeasonalPeThreshModal:{index},{dbuser.DiscordId}")
                    .WithButton("Cancel", $"MCSSeasonalPe:{index},{dbuser.DiscordId}")
                    .Build();
                await modal.UpdateAsync(x => { x.Content = null; x.Components = components; x.Embed = embed; });
            } else {
                account.SeasonalPeThreshold = num;
                dbuser.UpdateAccounts();
                await db.SaveChangesAsync();
                await modal.UpdateAsync(x => { x.Components = GetSeasonalPeComponents(index, account, dbuser); x.Embed = SeasonalPeEmbed(dbuser, account).Build(); });
            }
        }

        #endregion

        #region TwoToThree
        [ComponentCommand]
        public static async Task MCSTwoToThree(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            await component.UpdateAsync(x => { x.Components = TwoToThreeComponents(dbuser, account.DoTwoToThreeContracts, index); x.Embed = TwoToThreeEmbed(dbuser, account, account.DoTwoToThreeContracts); });
        }

        [ComponentCommand]
        public static async Task MCSToggleTwoToThree(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var toggleState = data.Split(",")[2] == "t";

            account.DoTwoToThreeContracts = toggleState;
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();

            await component.UpdateAsync(x => { x.Components = TwoToThreeComponents(dbuser, toggleState, index); x.Embed = TwoToThreeEmbed(dbuser, account, toggleState); });
        }

        [ComponentCommand]
        public static MessageComponent TwoToThreeComponents(DBUser dbuser, bool enabled, int index) {
            var builder = new ComponentBuilder();
            var row = new ActionRowBuilder()
                .WithButton(enabled ? "Disable 2 -> 3 Auto-Assignments" : "Enable 2 -> 3 Auto-Assignments", $"MCSToggleTwoToThree:{index},{dbuser.DiscordId},{(enabled ? "f" : "t")}")
                .WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}");
            builder.AddRow(row);
            return builder.Build();
        }

        [ComponentCommand]
        public static Embed TwoToThreeEmbed(DBUser dbuser, EggIncAccount account, bool enabled) {
            var twoToThreeMessage = $"Ocasionally, Leggacy Contracts will be released with three rewards, despite previously having two rewards. In your contract history, this will appear as a complete contract, and auto-assignment will not happen, by default.\n" +
                $"\n- If set to `No`, you will not be assigned coops for contracts in which only a new third reward is offered." +
                $"\n- If set to `Yes`, you will be automatically assigned a co-op for these \"`2 -> 3`\" Leggacy Contracts.";

            return MenuEmbedTemplate("2 -> 3 Contract Reward Menu", twoToThreeMessage, account, dbuser).AddField("Auto-Assign 2 -> 3 Contracts", enabled ? "Yes" : "No").Build();
        }
        #endregion

        #region Colleggtibles
        [ComponentCommand]
        public static async Task MCSColleggtible(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var embed = await ColleggtiblesEmbed(db, dbuser, account, account.DoUnfinishedCollegtibles);
            await component.UpdateAsync(x => { x.Components = ColleggtiblesComponents(dbuser, account.DoUnfinishedCollegtibles, index); x.Embed = embed; });
        }

        [ComponentCommand]
        public static async Task MCSToggleColleggtible(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var toggleState = data.Split(",")[2] == "t";

            account.DoUnfinishedCollegtibles = toggleState;
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();

            var embed = await ColleggtiblesEmbed(db, dbuser, account, toggleState);
            await component.UpdateAsync(x => { x.Components = ColleggtiblesComponents(dbuser, toggleState, index); x.Embed = embed; });
        }

        [ComponentCommand]
        public static MessageComponent ColleggtiblesComponents(DBUser dbuser, bool enabled, int index) {
            var builder = new ComponentBuilder();
            var row = new ActionRowBuilder()
                .WithButton(enabled ? "Disable Colleggtible Auto-Assignments" : "Enable Colleggtible Auto-Assignments", $"MCSToggleColleggtible:{index},{dbuser.DiscordId},{(enabled ? "f" : "t")}")
                .WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}");
            builder.AddRow(row);
            return builder.Build();
        }

        [ComponentCommand]
        public static async Task<Embed> ColleggtiblesEmbed(ApplicationDbContext db, DBUser dbuser, EggIncAccount account, bool enabled) {
            var customEggs = await db.GetCustomEggsAsync();
            var colleggtiblesMessage = $"Colleggtibles are **[Custom Eggs](<https://egg-inc.fandom.com/wiki/Colleggtibles>)** that reward permanent buffs when you achieve certain habitat populations farming a contract of that egg. " +
                $"Each Colleggtible egg has 4 levels, which all provide the same type of buff, at different efficacies. Levels unlock at:\n- Level 1: **10 Million** :chicken:\n- Level 2: **100 Million** :chicken:\n- Level 3: **1 Billion** :chicken:\n- Level 4: **10 Billion** :chicken:\n\n" +
                $"**__Your colleggtibles__**\n\n{getAccountColleggtibles(account.Backup, customEggs)}\n" +
                $"You can enable this option to be automatically assigned to all Colleggtible Contracts that you do not have at max level already.";

            return MenuEmbedTemplate("Colleggtibles Contract Menu", colleggtiblesMessage, account, dbuser).AddField("Auto-Assign Colleggtibles", enabled ? "Yes" : "No").Build();
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
            var dimensionString = firstMod.GetReadbleGameDimnension().Replace("_", " ").ToLowerInvariant().Titleize();
            return $"({((firstMod.Value < 1) ? "-" : "+")} {dimensionString}{(egg.Released ? "" : " \\*")})";
        }

        private static string GetModifierString(DBCustomEggModifier modifier) {
            var value = modifier.Value;
            var negative = false;
            if(value < 1) {
                value = 1 - value;
                negative = true;
            } else {
                value -= 1;
            }
            var dimensionString = modifier.GetReadbleGameDimnension().Replace("_", " ").ToLowerInvariant().Titleize();

            return $"{(negative ? "-" : "+")}{(int)(value * 100)}% {dimensionString}";
        }
        #endregion

        #region UltraPings
        [ComponentCommand]
        public static async Task MCSUltraPing(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            await component.UpdateAsync(x => { x.Components = UltraPingComponents(dbuser, account.PingForNCUltra, index); x.Embed = UltraPingEmbed(dbuser, account, account.PingForNCUltra); });
        }

        [ComponentCommand]
        public static async Task MCSUltraPingToggle(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var toggleState = data.Split(",")[2] == "t";

            account.PingForNCUltra = toggleState;
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();

            await component.UpdateAsync(x => { x.Components = UltraPingComponents(dbuser, toggleState, index); x.Embed = UltraPingEmbed(dbuser, account, toggleState); });
        }

        [ComponentCommand]
        public static MessageComponent UltraPingComponents(DBUser dbuser, bool enabled, int index) {
            var builder = new ComponentBuilder();
            var row = new ActionRowBuilder()
                .WithButton(enabled ? "Disable Pings" : "Enable Pings", $"MCSUltraPingToggle:{index},{dbuser.DiscordId},{(enabled ? "f" : "t")}")
                .WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}");
            builder.AddRow(row);
            return builder.Build();
        }

        [ComponentCommand]
        public static Embed UltraPingEmbed(DBUser dbuser, EggIncAccount account, bool enabled) {
            var ultraPingMessage = $"For Account {account.Backup?.UserName ?? "[unnamed]"} {account.Backup?.EarningsBonus.ToEggString()}\n\nThis option allows you to be notified when a Leggacy PE <:Egg_of_Prophecy_PE:669981330477547580> Contract that you have not finished, is offered to <:ultra:1131045418319495369> Egg, Inc. Ultra players. " +
                "These pings will occur when Ultra Contracts are released, on Fridays at " +
                $"<t:{new DateTimeOffset(2023, 5, 1, 11, 0, 0, TimeSpan.FromHours(-5)).ToUnixTimeSeconds()}:t>.";

            return MenuEmbedTemplate("Ultra Offer Pings Menu", ultraPingMessage, account, dbuser).AddField("Ultra Offer Pings", enabled ? "Enabled" : "Disabled").Build();
        }
        #endregion

        #region Break
        [ComponentCommand]
        public static async Task MCSBreak(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var builder = MCSBreakBuilder(account, index, dbuser);
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Embed = BreakEmbed(dbuser, account); });
        }

        public static Embed BreakEmbed(DBUser user, EggIncAccount account) {
            var templateString = "Setting a break will prevent you from being added to coops for the duration of the break.";
            var builder = MenuEmbedTemplate("Break Menu", templateString, account, user);

            if(user.GuildId == 656455567858073601 || user.GuildId == 1108127105088241746) { // Palace / Dev E9K
                var warning = $"```This is for when you need a break from all contracts;\nIt is NOT a break for coop assignments from this server.```";
                builder.AddField("⚠️ NOTE", warning);
            }

            return builder.AddField("Break", MCSBreakMessage(account)).Build();
        }

        private static ComponentBuilder MCSBreakBuilder(EggIncAccount account, int index, DBUser dbuser) {
            var builder = new ComponentBuilder();
            var row = new ActionRowBuilder();

            if(account.OnBreakUntil < DateTime.Now.AddDays(60) || account.OnBreakUntil == default) {
                row.WithButton("Add 1 Day to Break", $"BreakAddDay:{index},{dbuser.DiscordId}")
                   .WithButton("Add 1 Week to Break", $"BreakAddWeek:{index},{dbuser.DiscordId}");
            }

            if(account.OnBreakUntil != default && account.OnBreakUntil > DateTimeOffset.UtcNow) {
                row.WithButton("Stop Break Early", $"StopBreakEarly:{index},{dbuser.DiscordId}");
            }

            row.WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}");
            builder.AddRow(row);
            return builder;
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

        [ComponentCommand]
        public static async Task BreakAddDay(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            //Add 1 day to the DTO
            account.SetBreak(AddCappedDays(account.OnBreakUntil == default || account.OnBreakUntil < DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : account.OnBreakUntil, 1), dbuser);
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Embed = x.Embed = BreakEmbed(dbuser, account); x.Components = MCSBreakBuilder(account, index, dbuser).Build(); });
        }

        [ComponentCommand]
        public static async Task BreakAddWeek(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            //Add 7 days to the DTO
            account.SetBreak(AddCappedDays(account.OnBreakUntil == default || account.OnBreakUntil < DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : account.OnBreakUntil, 7), dbuser);
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Embed = x.Embed = BreakEmbed(dbuser, account); x.Components = MCSBreakBuilder(account, index, dbuser).Build(); });
        }

        [ComponentCommand]
        public static async Task StopBreakEarly(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            //default OnBreakUntil
            account.SetBreak(default, dbuser);
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Embed = x.Embed = BreakEmbed(dbuser, account); x.Components = MCSBreakBuilder(account, index, dbuser).Build(); });
        }

        private static DateTimeOffset AddCappedDays(DateTimeOffset currentDtOffset, int daysToAdd) {
            var dayDifferential = (currentDtOffset - DateTimeOffset.UtcNow).Days;
            if(dayDifferential >= 60) return currentDtOffset;
            else {
                if(dayDifferential + daysToAdd >= 60) daysToAdd = 60 - dayDifferential; //Cap to 60 days
                return currentDtOffset.AddDays(daysToAdd);
            }
        }

        #endregion

        #region Rewards
        [ComponentCommand]
        public static async Task MCSRewards(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var builder = new ComponentBuilder();
            account.AutoRegisterRewards ??= [];

            var select2 = new SelectMenuBuilder()
                .WithCustomId($"MCSRewardsSet:{index},{dbuser.DiscordId}")
                .WithPlaceholder("Rewards Filter")
                .WithMinValues(0).WithMaxValues(GetRewardDictionary().Count);
            foreach(var item in GetRewardDictionary()) {
                select2.AddOption(item.Value, ((int)item.Key).ToString(), isDefault: account.AutoRegisterRewards.Any(x => x == item.Key));
            }
            builder.WithSelectMenu(select2);
            if(account.AutoRegisterRewards != null && account.AutoRegisterRewards.Count > 0)
                builder.WithButton("Clear Filter (Do all contracts)", $"MCSRewardsClear:{index},{dbuser.DiscordId}");
            builder.WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}");
            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Embed = RewardsEmbed(dbuser, account); });
        }

        private static Embed RewardsEmbed(DBUser dbuser, EggIncAccount account) {
            var content = $"**This filter will apply to New Contracts & Leggacy Contracts by default. To set a filter specific for leggacies, use `Leggacy Rewards Filter`.**" +
                $"\n\nIf you only want to do contracts with certain rewards, please select those rewards below. You won't be automatically added to any contract that doesn't contain those rewards. If you select Clear Filter it'll set you to do all contracts regardless of rewards.";
            return MenuEmbedTemplate("Rewards Filter Menu", content, account, dbuser).Build();
        }

        [ComponentCommand]
        public static async Task MCSRewardsSet(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, ILogger logger) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var reg = dbuser.EggIncAccounts[index];

            reg.AutoRegisterRewards = component.Data.Values.Select(x => (Ei.RewardType)Enum.Parse(typeof(Ei.RewardType), x)).ToList();
            if(reg.AutoRegisterRewards.Any(x => x == Ei.RewardType.UnknownReward)) {
                reg.AutoRegisterRewards = [];
            }
            logger.LogInformation("{user}'s rewards updated to {list}", dbuser.DiscordUsername, string.Join(",", reg.AutoRegisterRewards.Select(r => r.ToString())));
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        [ComponentCommand]
        public static async Task MCSRewardsClear(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var reg = dbuser.EggIncAccounts[index];
            reg.AutoRegisterRewards = [];
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region LeggacyRewards
        [ComponentCommand]
        public static async Task MCSLeggacyRewards(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];
            var builder = new ComponentBuilder();
            account.LeggacyAutoRegisterRewards ??= [];

            var select2 = new SelectMenuBuilder()
                .WithCustomId($"MCSLeggacyRewardsSet:{index},{dbuser.DiscordId}")
                .WithPlaceholder("Leggacy Rewards Filter")
                .WithMinValues(0).WithMaxValues(GetRewardDictionary().Count);
            foreach(var item in GetRewardDictionary()) {
                select2.AddOption(item.Value, ((int)item.Key).ToString(), isDefault: account.LeggacyAutoRegisterRewards.Any(x => x == item.Key));
            }
            builder.WithSelectMenu(select2);
            if(account.LeggacyAutoRegisterRewards != null && account.LeggacyAutoRegisterRewards.Count > 0)
                builder.WithButton("Clear Filter (Follow main filter)", $"MCSLeggacyRewardsClear:{index},{dbuser.DiscordId}");
            builder.WithButton("Return", $"MCSMenu:{index},{dbuser.DiscordId}");
            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Embed = LeggacyRewardsEmbed(dbuser, dbuser.EggIncAccounts[index]); });
        }

        private static Embed LeggacyRewardsEmbed(DBUser dbuser, EggIncAccount account) {
            var content = $"**This filter applies _only_ to Leggacy Contracts. If it is empty, your normal filter will be applied instead**." +
                $"\n\nIf you only want to do contracts with certain rewards, please select those rewards below. You won't be automatically added to any contract that doesn't contain those rewards. If you select Clear Filter it'll set you to do all contracts regardless of rewards.";
            return MenuEmbedTemplate("Leggacy Rewards Filter Menu", content, account, dbuser).Build();
        }

        [ComponentCommand]
        public static async Task MCSLeggacyRewardsSet(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, ILogger logger) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var reg = dbuser.EggIncAccounts[index];

            reg.LeggacyAutoRegisterRewards = component.Data.Values.Select(x => (Ei.RewardType)Enum.Parse(typeof(Ei.RewardType), x)).ToList();
            if(reg.LeggacyAutoRegisterRewards.Any(x => x == Ei.RewardType.UnknownReward)) {
                reg.LeggacyAutoRegisterRewards = [];
            }
            logger.LogInformation("{user}'s leggacy rewards updated to {list}", dbuser.DiscordUsername, string.Join(",", reg.LeggacyAutoRegisterRewards.Select(r => r.ToString())));
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        [ComponentCommand]
        public static async Task MCSLeggacyRewardsClear(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var reg = dbuser.EggIncAccounts[index];
            reg.LeggacyAutoRegisterRewards = [];
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region Guild
        [ComponentCommand]
        public static async Task MCSGuild(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : component.User.Id));
            var index = int.Parse(data.Split(",")[0]);
            var account = dbuser.EggIncAccounts[index];

            var modal = new ModalBuilder().WithTitle("Enter Guild Name (leave blank for none)").WithCustomId($"MCSGuildUpdate:{index},{dbuser.DiscordId}")
                .AddTextInput(label: $"Enter Guild Name (leave blank for none)", value: account.Guild, customId: "name", required: false).Build();

            await component.RespondWithModalAsync(modal);

        }

        [Modal]
        public static async Task MCSGuildUpdate(SocketModal modal, [ComponentData] string data, ApplicationDbContext db) {
            var name = modal.Data.Components.First(x => x.CustomId == "name").Value;
            var bypassUserId = data.Split(",").Length > 0 ? Convert.ToUInt64(data.Split(",")[1]) : 0;
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == (bypassUserId != 0 ? bypassUserId : modal.User.Id));
            var index = int.Parse(data.Split(",")[0]);

            var account = dbuser.EggIncAccounts[index];
            var guildNameDifferent = account.Guild != name.Truncate(100);
            account.Guild = name.Truncate(100);
            var changed = dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var mainMenu = MainMenu(dbuser, account, index, db.CachedGuilds.FirstOrDefault(x => x.Id == dbuser.GuildId));
            if(!changed && !guildNameDifferent) {
                await modal.UpdateAsync(x => {
                    x.Content = mainMenu.Content.GetValueOrDefault(null); x.Components = mainMenu.Components.GetValueOrDefault(); x.Embeds = new Embed[] { mainMenu.Embed.GetValueOrDefault(null), new EmbedBuilder().WithColor(Color.Red).WithTitle("No changes were made").WithDescription("No changes were made but were supposed to, please try again. (Kendrome is attempting to figure out why this happening to fix it)").Build() };
                });
            } else {
                await modal.UpdateAsync(x => { x.Content = mainMenu.Content.GetValueOrDefault(null); x.Components = mainMenu.Components.GetValueOrDefault(); x.Embed = mainMenu.Embed.GetValueOrDefault(null); });
            }
        }
        #endregion
    }
}
