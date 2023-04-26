using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Bot.Commands;
using Discord.Rest;
using Humanizer;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Helpers;
using Ei;
using EGG9000.Common.Migrations;
using Polly;
using Microsoft.Data.SqlClient;
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using EGG9000.Common.Commands;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using static EGG9000.Common.Database.Entities.UpcomingContract;

namespace EGG9000.Bot.Automated {
    public class UpcomingContracts : _UpdaterBase<UpcomingContracts> {
        public UpcomingContracts(
            IServiceProvider provider
        ) : base(TimeSpan.FromMinutes(30), TimeSpan.Zero, provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var upcomingContracts = await _db.UpcomingContracts.Where(x => x.ContractId == null).ToListAsync();

            var contractSchedule = new List<(DateTimeOffset date, bool IsLeggacy)> {
                (GetNextWeekday(DayOfWeek.Monday), false),
                (GetNextWeekday(DayOfWeek.Wednesday), true),
                (GetNextWeekday(DayOfWeek.Friday), true),
            };

            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();

            foreach(var c in contractSchedule.OrderBy(x => x.date)) {
                foreach(var dbguild in dbguilds.Where(x => x.Id == 656455567858073601)) {
                    if(!upcomingContracts.Any(x => x.TargetDate == c.date && x.GuildID == dbguild.Id)) {
                        try {
                            var guild = _client.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                            var upcomingContract = new UpcomingContract { IsLeggacy = c.IsLeggacy, TargetDate = c.date, GuildID = guild.Id };
                            var contractCategory = await _client.GetCategoryAsync(GuildChannelType.EliteCategory, guild);
                            var channel = await guild.CreateTextChannelAsync($"🆕{c.date:ddd-MMM-dd}", x => x.CategoryId = contractCategory.Id);
                            await channel.AddPermissionOverwriteAsync(guild.EveryoneRole, OverwritePermissions.DenyAll(channel));
                            var builder = new ComponentBuilder().WithButton("Register For Contract", "UC_Register");
                            await channel.SendMessageAsync($"Register for the upcoming contract on {c.date:dddd, MMMM dd}", components: builder.Build());
                            upcomingContract.ChannelId = channel.Id;
                            _db.Add(upcomingContract);
                            await _db.SaveChangesAsync();
                        } catch(Exception e) {
                            _bugsnag.Notify(e);
                        }
                    }
                }
            }
        }

        private static DateTimeOffset GetNextWeekday(DayOfWeek day) {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            var today = DateTimeOffset.Now.AddDays(1);
            var start = new DateTimeOffset(today.Year, today.Month, today.Day, 10, 0, 0, tz.BaseUtcOffset);
            // The (... + 7) % 7 ensures we end up with a value in the range [0, 6]
            int daysToAdd = ((int)day - (int)start.DayOfWeek + 7) % 7;
            return start.AddDays(daysToAdd);
        }

        [ComponentCommand]
        public static async Task UC_Register(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == component.User.Id);
            if(dbuser == null) {
                await component.RespondAsync("ERROR: Unable to find user, are you registered?", ephemeral: true);
            } else if(dbuser.EggIncAccounts.Count > 1) {
                await component.RespondAsync("Select which account you would like to manage", components: ContractSettingsCommands.GetAccountButtons(dbuser,"UCMenu"));
            } else {
                var uc = await db.UpcomingContracts.FirstAsync(x => x.ChannelId == component.ChannelId);
                var a = uc.UserRegisters.FirstOrDefault(x => x.UserID == dbuser.Id);
                var props = MainMenu(dbuser, dbuser.EggIncAccounts.First(), a, 0);
                await component.RespondAsync(props.Content.GetValueOrDefault(null), components: props.Components.GetValueOrDefault(null), embed: props.Embed.GetValueOrDefault(null));//, ephemeral: true);
            }
        }

        [ComponentCommand]
        public static async Task UCMenu(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var index = int.Parse(data);
            var account = dbuser.EggIncAccounts[index];
            var uc = await db.UpcomingContracts.FirstAsync(x => x.ChannelId == component.ChannelId);
            var a = uc.UserRegisters.FirstOrDefault(x => x.UserID == dbuser.Id && x.EggIncId == account.Id);
            var props = MainMenu(dbuser, dbuser.EggIncAccounts[index], a, index);
            await component.UpdateAsync(x => { x.Content = props.Content.GetValueOrDefault(null); x.Components = props.Components.GetValueOrDefault(null); x.Embed = props.Embed.GetValueOrDefault(null); });
        }

        public static MessageProperties MainMenu(DBUser dbuser, DBUser.EggIncAccount account, UserRegister register, int index) {
            if(register is null)
                register = new UserRegister();


            var eBuilder = new EmbedBuilder()
                .WithTitle($"Main Menu");
            var builder = new ComponentBuilder();

            if(dbuser.EggIncAccounts.Count > 1) {
                var backup = dbuser.Backups.FirstOrDefault(x => x.EggIncId == account.Id);
                eBuilder.WithDescription($"For Account {(string.IsNullOrWhiteSpace(account.Name) ? "[unnamed]" : account.Name)} {backup?.EarningsBonus.ToEggString()}");
            }

            if(account.OnBreakUntil > DateTimeOffset.Now) {
                eBuilder.AddField("On Break Until", new TimestampTag(DateTimeOffset.Now, TimestampTagStyles.LongDate));
                builder.WithButton("Remove Break", $"UCRemoveBreak:{index}");
            } else if(account.Group != default && !register.Skip) {
                eBuilder.Description += "\n\nYou are set to assigned this contract assuming it meets your settings.";
                builder.WithButton("Skip This Contract", $"UCSkip:{index}");
            } else if(account.Group != default) {
                eBuilder.Description += "\n\nYou are set to skip this contract.";
                builder.WithButton("Undo Skip", $"UCUndoSkip:{index}");
            } else {
                eBuilder.AddField("Boarding Group", register.Group > 0 ? register.Group.ToString() : "None");
                var rDict = ContractSettingsCommands.GetRewardDictionary();
                if(account.AutoRegisterRewards is null)
                    account.AutoRegisterRewards = new List<Ei.RewardType>();
                eBuilder.AddField("Rewards Filter", account.AutoRegisterRewards.Any() ? string.Join(",", account.AutoRegisterRewards.Select(x => rDict[x])) : "All Contracts");

                builder.WithButton("Boarding Group", $"MCSBg:{index}")
                .WithButton("Set Break", $"MCSBreak:{index}")
                .WithButton("Rewards Filter", $"MCSRewards:{index}")
                .WithButton("Redo Leggacies", $"MCS_Redo:{index}");

            }

            if(dbuser.EggIncAccounts.Count > 1)
                builder.WithButton("Return", $"MCSAccounts");

            var props = new MessageProperties();
            props.Components = builder.Build();
            props.Embed = eBuilder.Build();
            return props;
        }
    }
}