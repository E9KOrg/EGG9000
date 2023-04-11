using Discord;
using Discord.Webhook;
using Discord.WebSocket;

using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Google.Protobuf;

using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace EGG9000.Bot.Commands {
    public class ContractSettingsCommands {
        [SlashCommand(Description = "My Contract Settings")]
        public static async Task MyContractSettings(FauxCommand command, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbuser == null) {
                await command.RespondAsync("ERROR: Unable to find user");
            } else {
                var props = GetMyContractSettingsMessage(dbuser);
                await command.RespondAsync(props.Content.Value, components: props.Components.Value);//, ephemeral: true);
            }
        }

        public static MessageProperties GetMyContractSettingsMessage(DBUser dbuser) {
            var props = new MessageProperties();
            var reg = dbuser.ContractRegistration;

            if(reg.AutoRegister) {
                props.Content = $@"**My Contract Settings**
You are setup to automatically be assigned co-ops.
Configure which Boarding Group and if you only want contracts with certain rewards below:
BG1 <t:1681138800:t>, BG2 <t:1681167600:t>, BG3 <t:1681196400:t>. BG1 is when new contracts usually come.
";

                var select1 = new SelectMenuBuilder()
                    .WithCustomId("BoardingGroup")
                    .WithPlaceholder("Select Boarding Group")
                    .AddOption("Group 1", "1", isDefault: reg.Group == 1).AddOption("Group 2", "2", isDefault: reg.Group == 2).AddOption("Group 3", "3", isDefault: reg.Group == 3);
                var builder = new ComponentBuilder().WithSelectMenu(select1);
                if(reg.EnableFilter) {
                    var select2 = new SelectMenuBuilder()
                        .WithCustomId("Rewards")
                        .WithPlaceholder("Rewards Filter")
                        .WithMinValues(0).WithMaxValues(5)
                        .AddOption("Eggs Of Prophecy", ((int)Ei.RewardType.EggsOfProphecy).ToString(), isDefault: reg.AutoRegisterRewards.Any(x => x == Ei.RewardType.EggsOfProphecy))
                        .AddOption("Artifacts", ((int)Ei.RewardType.Artifact).ToString(), isDefault: reg.AutoRegisterRewards.Any(x => x == Ei.RewardType.Artifact))
                        .AddOption("Piggy Bank", ((int)Ei.RewardType.PiggyMultiplier).ToString(), isDefault: reg.AutoRegisterRewards.Any(x => x == Ei.RewardType.PiggyMultiplier))
                        .AddOption("Shell Tickets", ((int)Ei.RewardType.ShellScript).ToString(), isDefault: reg.AutoRegisterRewards.Any(x => x == Ei.RewardType.ShellScript))
                        .AddOption("Clear Filter (Do All Contracts)", "AllContracts")
                        ;
                    builder.WithSelectMenu(select2);
                } else {
                    builder.WithButton("Filter Contract By Reward", "EnableFilter");
                }
                //Only show "take a break" if the user isn't already on one
                builder.WithButton("Disable Auto Assigned Co-ops", "DisableAutoAssign");

                if(reg.OnBreakUntil == default) {
                    builder.WithButton("Take a Break From Co-ops", "TakeABreak");
                } else {
                    //Else show some options (add 1 day, add 1 week, stop early)
                    props.Content += $"\nBreak ends <t:{reg.OnBreakUntil.ToUnixTimeSeconds()}:R> on <t:{reg.OnBreakUntil.ToUnixTimeSeconds()}:D>\n";
                    var row = new ActionRowBuilder()
                    .WithButton("Add 1 Day to Break", "BreakAddDay")
                    .WithButton("Add 1 Week to Break", "BreakAddWeek")
                    .WithButton("Stop Break Early", "StopBreakEarly");
                    builder.AddRow(row);
                }
                props.Components = builder.Build();
            } else {
                props.Content = $@"**My Contract Settings**
You are not configured to automatically be assigned co-ops.
";
                var builder = new ComponentBuilder().WithButton("Enable Auto Assigned Co-ops", "EnableAutoAssign");
                props.Components = builder.Build();
            }
            return props;
        }

        [ComponentCommand]
        public static async Task EnableAutoAssign(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            reg.AutoRegister = true;
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }

        [ComponentCommand]
        public static async Task DisableAutoAssign(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            reg.AutoRegister = false;
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }
        [ComponentCommand]
        public static async Task EnableFilter(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            reg.EnableFilter = true;
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }

        [ComponentCommand]
        public static async Task BreakAddDay(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            //Add 1 day to the DTO
            reg.OnBreakUntil = reg.OnBreakUntil.AddDays(1);
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }

        [ComponentCommand]
        public static async Task BreakAddWeek(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db)
        {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            //Add 7 days to the DTO
            reg.OnBreakUntil = reg.OnBreakUntil.AddDays(7);
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }

        [ComponentCommand]
        public static async Task TakeABreak(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db)
        {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            //Initialize with a single day
            reg.OnBreakUntil = new DateTimeOffset(DateTime.UtcNow.AddDays(1));
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }

        [ComponentCommand]
        public static async Task StopBreakEarly(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            //default OnBreakUntil
            reg.OnBreakUntil = default;
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }

        [ComponentCommand]
        public static async Task BoardingGroup(SocketMessageComponent component, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            reg.Group = byte.Parse(component.Data.Values.First());
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }

        [ComponentCommand]
        public static async Task Rewards(SocketMessageComponent component, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var reg = dbuser.ContractRegistration;
            if(component.Data.Values.Any(x => x == "AllContracts")) {
                reg.EnableFilter = false;
            } else {
                reg.AutoRegisterRewards = component.Data.Values.Select(x => (Ei.RewardType)Enum.Parse(typeof(Ei.RewardType), x)).ToList();
            }
            dbuser.ContractRegistration = reg;
            await db.SaveChangesAsync();
            var props = GetMyContractSettingsMessage(dbuser);
            await component.UpdateAsync(x => { x.Content = props.Content; x.Components = props.Components; });
        }
    }
}
