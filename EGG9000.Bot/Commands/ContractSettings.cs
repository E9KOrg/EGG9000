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

using Microsoft.EntityFrameworkCore;
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
        #region MainMenu
        [SlashCommand(Description = "My Contract Settings")]
        public static async Task MyContractSettings(FauxCommand command, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbuser == null) {
                await command.RespondAsync("ERROR: Unable to find user, are you registered?", ephemeral: true);
            } else if(dbuser.EggIncAccounts.Count > 1) {
                await command.RespondAsync("Select which account you would like to manage", components: GetAccountButtons(dbuser, "MCSMenu"), ephemeral: true);
            } else {
                var props = MainMenu(dbuser, dbuser.EggIncAccounts.First(), 0);
                await command.RespondAsync(props.Content.GetValueOrDefault(null), components: props.Components.GetValueOrDefault(null), embed: props.Embed.GetValueOrDefault(null), ephemeral: true);
            }
        }

        [ComponentCommand]
        public static async Task MCSAccounts(SocketMessageComponent component, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            await component.UpdateAsync(x => { x.Content = "Select which account you would like to manage"; x.Components = GetAccountButtons(dbuser, "MCSMenu"); x.Embed = null; });
        }

        public static MessageComponent GetAccountButtons(DBUser dbuser, string prefix) {
            var builder = new ComponentBuilder();
            for(var i = 0; i < dbuser.EggIncAccounts.Count; i++) {
                var account = dbuser.EggIncAccounts[i];
                var backup = dbuser.Backups.FirstOrDefault(x => x.EggIncId == account.Id);
                builder.WithButton($"{(string.IsNullOrWhiteSpace(account.Name) ? "[unnamed]" : account.Name)} {backup?.EarningsBonus.ToEggString()}", $"{prefix}:{i}");
            }
            return builder.Build();
        }
        public static MessageProperties MainMenu(DBUser dbuser, DBUser.EggIncAccount account, int index) {
            var props = new MessageProperties();

            var eBuilder = new EmbedBuilder()
                .WithTitle($"Main Menu");

            if(dbuser.EggIncAccounts.Count > 1) {
                var backup = dbuser.Backups.FirstOrDefault(x => x.EggIncId == account.Id);
                eBuilder.WithDescription($"For Account {(string.IsNullOrWhiteSpace(account.Name) ? "[unnamed]" : account.Name)} {backup?.EarningsBonus.ToEggString()}");
            }
            eBuilder.AddField("Boarding Group", account.Group != default ? account.Group.ToString() : "Not Set (please select below)");
            eBuilder.AddField("Break", account.OnBreakUntil == default ? "Not on break" : $"Ends <t:{account.OnBreakUntil.ToUnixTimeSeconds()}:R>");
            var rDict = GetRewardDictionary();
            if(account.AutoRegisterRewards is null)
                account.AutoRegisterRewards = new List<Ei.RewardType>();
            eBuilder.AddField("Rewards Filter", account.AutoRegisterRewards.Any() ? string.Join(",", account.AutoRegisterRewards.Select(x => rDict[x])) : "All Contracts");

            //eBuilder.AddField("Redo Completed Leggacies", account.RedoLeggacy ? "Yes (Will redo all contracts to help out others)" : "No (Will still be assigned to incomplete leggacies)");
            var redoText = account.RedoLeggacy?.menuText ?? "No (Will still be assigned to incomplete leggacies)";
            eBuilder.AddField("Redo Completed Leggacies", redoText);

            var builder = new ComponentBuilder()
                .WithButton("Boarding Group", $"MCSBg:{index}")
                .WithButton("Set Break", $"MCSBreak:{index}")
                .WithButton("Rewards Filter", $"MCSRewards:{index}")
                .WithButton("Redo Completed Leggacies", $"MCSRL:{index}");
            if(dbuser.EggIncAccounts.Count > 1)
                builder.WithButton("Return", $"MCSAccounts");
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
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
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
            var content = $"Boarding Groups (BG) set when your co-op will be launched when a contract comes out. Select which BG will allow you to be most active after a co-op is launched at that time.\n\nHere are BG times in your local timezone:\nBG1 <t:1681142400:t>  (When contracts normally launch)\n BG2 <t:1681171200:t>\n BG3 <t:1681200000:t>";
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
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion

        #region RedoLeggacies
        [ComponentCommand]
        public static async Task MCSRL(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            var builder = new ComponentBuilder().WithSelectMenu($"MCSRedoLeggacies:{index}", new List<SelectMenuOptionBuilder> {
                new SelectMenuOptionBuilder("Yes (Will redo all contracts to help out others)", "1", isDefault: account.RedoLeggacy.type == RedoType.YesAll),
                new SelectMenuOptionBuilder($"Yes (If previous score was under {account.RedoScoreThreshold} score)", "2", isDefault: account.RedoLeggacy.type == RedoType.YesThreshold),
                new SelectMenuOptionBuilder("No (Will still be assigned to incomplete leggacies)", "3", isDefault: account.RedoLeggacy.type == RedoType.No)
            });
            if(account.RedoLeggacy.type == RedoType.YesThreshold) {
                builder.WithContext($"Redo leggacy contracts under {account.RedoScoreThreshold} CS");
                if(account.RedoScoreThreshold >= 1000)
                    builder.WithButton("Decrease Threshold by 1000 CS", $"RLThreshDec:{index}");
                if(account.RedoScoreThreshold <= 79000)
                    builder.WithButton("Increase Threshold by 1000 CS", $"RLThreshInc:{index}");
            }

            builder.WithButton("Cancel", $"MCSMenu:{index}");
        }

        [ComponentCommand]
        public static async Task RLThreshDec(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            account.RedoScoreThreshold -= 1000;
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task RLThreshInc(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db)
        {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            account.RedoScoreThreshold += 1000;
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        [ComponentCommand]
        public static async Task MCSRedoLeggacies(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            account.RedoLeggacy = new RedoLeggacyOption(int.Parse(component.Data.Values.First()) - 1);
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        /*[ComponentCommand]
        public static async Task MCS_Redo(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var reg = dbuser.EggIncAccounts[index];
            reg.RedoLeggacy = !reg.RedoLeggacy;
            dbuser.UpdateAccounts();
            await db.SaveChangesAsync();
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }*/
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
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
            await component.UpdateAsync(x => { x.Components = builder.Build(); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        public static string MCSBreakMessage(DBUser.EggIncAccount account) {
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
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
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
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
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
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
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
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
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
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], index);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }
        #endregion
    }
}
