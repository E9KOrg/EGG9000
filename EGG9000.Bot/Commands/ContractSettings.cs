using Bugsnag.Payload;

using Discord;
using Discord.Webhook;
using Discord.WebSocket;

using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Google.Protobuf;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Net.Http.Headers;
using RazorEngine.Compilation.ImpromptuInterface.InvokeExt;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace EGG9000.Bot.Commands {
    public class ContractSettingsCommands {
        public static List<(int bg, long time)> BoardingGroupTimes = new List<(int bg, long time)> {
            (1, new DateTimeOffset(2023, 5, 1, 11, 0, 0 , TimeSpan.FromHours(-5)).ToUnixTimeSeconds()),
            (2, new DateTimeOffset(2023, 5, 1, 11, 0, 0 , TimeSpan.FromHours(-5)).AddHours(8).ToUnixTimeSeconds()),
            (3, new DateTimeOffset(2023, 5, 1, 11, 0, 0 , TimeSpan.FromHours(-5)).AddHours(16).ToUnixTimeSeconds()),
        };

        #region MainMenu
        [SlashCommand(Description = "My Contract Settings")]
        public static async Task MyContractSettings(FauxCommand command, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbuser == null) {
                await command.RespondAsync("ERROR: Unable to find user, are you registered?", ephemeral: !System.Diagnostics.Debugger.IsAttached);
            } else {
                await command.RespondAsync("Select which account you would like to manage", components: GetAccountButtons(dbuser, "MCSMenu"), ephemeral: !System.Diagnostics.Debugger.IsAttached);
            }
        }

        [ComponentCommand]
        public static async Task MCSAccounts(SocketMessageComponent component, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            await component.UpdateAsync(x => { x.Content = ""; x.Components = GetAccountButtons(dbuser, "MCSMenu"); x.Embed = null; });
        }

        public static MessageComponent GetAccountButtons(DBUser dbuser, string prefix) {
            var builder = new ComponentBuilder();
            for(var i = 0; i < dbuser.EggIncAccounts.Count; i++) {
                var account = dbuser.EggIncAccounts[i];
                builder.WithButton($"Manage {(string.IsNullOrWhiteSpace(account.Name) ? "[unnamed]" : account.Name)} {account.Backup?.EarningsBonus.ToEggString()}", $"{prefix}:{i}");
            }

            builder.WithButton("Coop Settings", "CSAccountMenu");
            builder.WithButton("Ship Return DM", "SRDMenu");
            return builder.Build();
        }
        public static MessageProperties MainMenu(DBUser dbuser, EggIncAccount account, int index, Guild dbguild) {
            var props = new MessageProperties();

            var eBuilder = new EmbedBuilder()
                .WithTitle($"Main Menu");

            if(dbuser.EggIncAccounts.Count > 1) {
                eBuilder.WithDescription($"For Account {(string.IsNullOrWhiteSpace(account.Name) ? "[unnamed]" : account.Name)} {account.Backup?.EarningsBonus.ToEggString()}");
            }
            if(!dbguild.DisableBG) {
                eBuilder.AddField("Boarding Group", account.Group != default ? $"BG{account.Group} Co-ops start just after <t:{BoardingGroupTimes.First(x => x.bg == account.Group).time}:t>" : "Not Set (please select below)");
            }
            eBuilder.AddField("Break", account.OnBreakUntil == default ? "Not on break" : $"Ends <t:{account.OnBreakUntil.ToUnixTimeSeconds()}:R>");
            if(!dbguild.DisableBG) {
                var rDict = GetRewardDictionary();
                if(account.AutoRegisterRewards is null)
                    account.AutoRegisterRewards = new List<Ei.RewardType>();
                eBuilder.AddField("Rewards Filter", account.AutoRegisterRewards.Any() ? string.Join(",", account.AutoRegisterRewards.Select(x => rDict[x])) : "All Contracts");
            }

            //eBuilder.AddField("Redo Completed Leggacies", account.RedoLeggacy ? "Yes (Will redo all contracts to help out others)" : "No (Will still be assigned to incomplete leggacies)");
            var redoText = account.RedoLeggacySelection switch {
                RedoLeggacyOption.YesAll => "Yes (Will redo all contracts to help out others)",
                RedoLeggacyOption.YesThreshold => $"Yes (If previous score was under {account.RedoScoreThreshold} score)",
                RedoLeggacyOption.No => "No (Will still be assigned to incomplete leggacies)",
                _ => "No (Will still be assigned to incomplete leggacies)"
            };
            eBuilder.AddField("Redo Completed Leggacies", redoText);

            var builder = new ComponentBuilder()
                .WithButton("Boarding Group", $"MCSBg:{index}")
                .WithButton("Set Break", $"MCSBreak:{index}")
                .WithButton("Rewards Filter", $"MCSRewards:{index}")
                .WithButton("Redo Completed Leggacies", $"MCSRL:{index}");
            builder.WithButton("Return", $"MCSAccounts", ButtonStyle.Secondary);
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
            };
        }

        [ComponentCommand]
        public static async Task MCSMenu(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region Boarding Group
        [ComponentCommand]
        public static async Task MCSBg(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            var builder = new ComponentBuilder().WithSelectMenu($"MCSBoardingGroup:{index}", new List<SelectMenuOptionBuilder> {
                new SelectMenuOptionBuilder("Group 1 (Contract Launch)", "1", isDefault: account.Group == 1),
                new SelectMenuOptionBuilder("Group 2", "2", isDefault: account.Group == 2),
                new SelectMenuOptionBuilder("Group 3", "3", isDefault: account.Group == 3),
            });
            builder.WithButton("Cancel", $"MCSMenu:{index}");
            var content = $"Boarding Groups (BG) set when your co-op will be launched when a contract comes out.Select which BG will allow you to be most active after a co-op is launched at that time.\n\nHere are BG times in your local timezone:\n BG1 <t:{BoardingGroupTimes[0].time}:t>  (When contracts normally launch)\n{string.Join("\n", BoardingGroupTimes.Skip(1).Select(x => $" BG{x.bg} <t:{x.time}:t>"))}";
            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Content = content; x.Embed = null; });
        }

        [ComponentCommand]
        public static async Task MCSBoardingGroup(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            account.Group = byte.Parse(component.Data.Values.First());
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region RedoLeggacies
        [ComponentCommand]
        public static async Task MCSRL(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];

            var mainMenu = MainMenu(dbuser, account, index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = mainMenu.Content.GetValueOrDefault(null); x.Components = GetRlButtons(index, account); x.Embed = mainMenu.Embed.GetValueOrDefault(null); });
        }

        private static MessageComponent GetRlButtons(int index, EggIncAccount account) {
            var builder = new ComponentBuilder().WithSelectMenu($"MCSRedoLeggacies:{index}", new List<SelectMenuOptionBuilder> {
                new SelectMenuOptionBuilder("Yes (Will redo all contracts to help out others)", "1", isDefault: account.RedoLeggacySelection == RedoLeggacyOption.YesAll),
                new SelectMenuOptionBuilder($"Yes (If your previous score was under a threshold you set)", "2", isDefault: account.RedoLeggacySelection == RedoLeggacyOption.YesThreshold),
                new SelectMenuOptionBuilder("No (Will still be assigned to incomplete leggacies)", "3", isDefault: account.RedoLeggacySelection == RedoLeggacyOption.No)
            });
            if(account.RedoLeggacySelection == RedoLeggacyOption.YesThreshold) {
                builder.WithContext($"Redo leggacy contracts under {account.RedoScoreThreshold} CS");
                builder.WithButton("Change CS Threshold", $"RLThreshModal:{index}");
            }

            builder.WithButton("Return", $"MCSMenu:{index}", ButtonStyle.Secondary);
            return builder.Build();
        }

        [ComponentCommand]
        public static async Task RLThreshModal(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];

            var modal = new ModalBuilder().WithTitle("Update CS Threshold").WithCustomId($"RlThreshUpdate:{index}")
                .AddTextInput(label: "Enter CS Threshold greater than 0", value: account.RedoScoreThreshold.ToString(), customId: "num", required: true).Build();

            await component.RespondWithModalAsync(modal);
        }

        [Modal]
        public static async Task RlThreshUpdate(SocketModal modal, [ComponentData] string data, ApplicationDbContext db)
        {
            var numText = modal.Data.Components.First(x => x.CustomId == "num").Value;
            var isNum = int.TryParse(numText, out int num);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == modal.User.Id);
            var index = int.Parse(data);
            if(!isNum || num <= 0) {
                var embedBuilder = new EmbedBuilder().WithTitle("⚠️Input needs to be a positive integer").WithColor(Color.Red).Build();
                var components = new ComponentBuilder().WithButton("Re-enter", $"RLThreshModal:{index}").WithButton("Cancel", $"MCSRL:{index}").Build();
                await modal.UpdateAsync(x => { x.Content = null; x.Components = components; x.Embed = embedBuilder; });
            } else {
                var account = dbuser.EggIncAccounts[index];
                account.RedoScoreThreshold = num;
                dbuser.UpdateAccounts();
                await db.SaveChangesAsync();

                var mainMenu = MainMenu(dbuser, account, index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
                await modal.UpdateAsync(x => { x.Content = mainMenu.Content.GetValueOrDefault(null); x.Components = GetRlButtons(index, account); x.Embed = mainMenu.Embed.GetValueOrDefault(null); });
            }
        }

        [ComponentCommand]
        public static async Task MCSRedoLeggacies(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            account.RedoLeggacySelection = component.Data.Values.First() switch {
                "1" => RedoLeggacyOption.YesAll,
                "2" => RedoLeggacyOption.YesThreshold,
                "3" => RedoLeggacyOption.No,
                _ => RedoLeggacyOption.No
            };
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = GetRlButtons(index, account); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region Break
        [ComponentCommand]
        public static async Task MCSBreak(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            var builder = new ComponentBuilder();
            var row = new ActionRowBuilder()
                .WithButton("Add 1 Day to Break", $"BreakAddDay:{index}")
                .WithButton("Add 1 Week to Break", $"BreakAddWeek:{index}")
                .WithButton("Stop Break Early", $"StopBreakEarly:{index}")
                .WithButton("Return", $"MCSMenu:{index}");
            builder.AddRow(row);
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        public static string MCSBreakMessage(EggIncAccount account) {
            if(account.OnBreakUntil == default) {
                return "Not currently on break";
            } else {
                return $"\nBreak ends <t:{account.OnBreakUntil.ToUnixTimeSeconds()}:R> on <t:{account.OnBreakUntil.ToUnixTimeSeconds()}:D>\n";
            }
        }

        [ComponentCommand]
        public static async Task BreakAddDay(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            //Add 1 day to the DTO
            account.OnBreakUntil = (account.OnBreakUntil == default ? DateTimeOffset.Now : account.OnBreakUntil).AddDays(1);
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task BreakAddWeek(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            //Add 7 days to the DTO
            account.OnBreakUntil = (account.OnBreakUntil == default ? DateTimeOffset.Now : account.OnBreakUntil).AddDays(7);
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task StopBreakEarly(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            //default OnBreakUntil
            account.OnBreakUntil = default;
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region Rewards
        [ComponentCommand]
        public static async Task MCSRewards(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var reg = dbuser.EggIncAccounts[index];
            var builder = new ComponentBuilder();
            if(reg.AutoRegisterRewards is null)
                reg.AutoRegisterRewards = new List<Ei.RewardType>();

            var select2 = new SelectMenuBuilder()
                .WithCustomId($"MCSRewardsSet:{index}")
                .WithPlaceholder("Rewards Filter")
                .WithMinValues(0).WithMaxValues(GetRewardDictionary().Count());
            foreach(var item in GetRewardDictionary()) {
                select2.AddOption(item.Value, ((int)item.Key).ToString(), isDefault: reg.AutoRegisterRewards.Any(x => x == item.Key));
            }
            builder.WithSelectMenu(select2);
            builder.WithButton("Clear Filter (Do all contracts)", $"MCSRewardsClear:{index}");
            var content = $"If you only want to do contracts with certain rewards, please select those rewards below. You won't be automatically added to any contract that doesn't contain those rewards. If you select Clear Filter it'll set you to do all contracts regardless of rewards.";
            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Embed = null; x.Content = content; });
        }

        [ComponentCommand]
        public static async Task MCSRewardsSet(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var reg = dbuser.EggIncAccounts[index];
            reg.AutoRegisterRewards = component.Data.Values.Select(x => (Ei.RewardType)Enum.Parse(typeof(Ei.RewardType), x)).ToList();
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        [ComponentCommand]
        public static async Task MCSRewardsClear(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var reg = dbuser.EggIncAccounts[index];
            reg.AutoRegisterRewards = new List<Ei.RewardType>();
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index, await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId));
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion
    }
}
