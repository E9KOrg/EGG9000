using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Helpers;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Services;
using EGG9000.Common.Commands;
using EGG9000.Common.Extensions;
using EGG9000.Common.JsonData.EiAfxData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using static Ei.Backup.Types;
using static EGG9000.Common.Helpers.FAQHelper;
using System.ComponentModel;
using RazorEngine.Compilation.ImpromptuInterface.InvokeExt;

namespace EGG9000.Bot.Commands {
    public static class MiscCommandsSlash {
        [SlashCommand(Description = "Track your EB since the last time you ran this command", AllowInDMs = true)]
        public static async Task TrackEB(FauxCommand command, ApplicationDbContext db, ILogger logger) {
            await command.RespondAsync("Getting backups...");
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var builder = new EmbedBuilder();
            builder.Title = $"EB Tracking";
            foreach(var id in user.EggIncAccounts) {
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await ContractsAPI.FirstContact(id.Id)).Backup);
                if(user.EggIncAccounts.Count > 1) {
                    builder.AddField("――――――――――――――――――", $"**{backup.UserName}**");
                }

                var backupDate = DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime);

                if(id.LastEBTime.HasValue) {
                    builder.AddField("Last EB", $"{id.LastEB.ToEggString()}\n{DiscordHelpers.TimeStamper(id.LastEBTime.Value, DiscordHelpers.DiscordTimestampFormat.Relative)}", true);
                }

                builder.AddField("Current EB", $"{backup.EarningsBonus.ToEggString()}\n{DiscordHelpers.TimeStamper(backupDate, DiscordHelpers.DiscordTimestampFormat.Relative)}", true);

                if(id.LastEBTime.HasValue) {
                    var change = backup.EarningsBonus - id.LastEB;
                    var percentChange = change / id.LastEB * 100d;
                    if(percentChange >= 10)
                        percentChange = Math.Round(percentChange);
                    var digits = (int)Math.Log10(percentChange) - 2;
                    var format = $"F{(digits < 1 ? -digits + 2 : 0)}";
                    var timeStampDifference = (id.LastEBTime.Value - backupDate).Humanize();
                    builder.AddField("EB Gained", $"{change.ToEggString()} (+{percentChange.ToString(format)}%)\n{timeStampDifference}", true);
                } else {
                    builder.AddField("First Update", "No previous EB to compare to", true);
                }

                id.LastEB = backup.EarningsBonus;
                id.LastEBTime = backupDate;
            }

            user.UpdateAccounts();
            await db.SaveChangesAsync();
            await command.ModifyOriginalResponseAsync(x => {
                x.Embed = builder.Build();
                x.Content = "";
            });
        }

        [SlashCommand(Description = "How many SE/PE needed for next rank up", AllowInDMs = true)]
        public static async Task NextRank(FauxCommand command, ApplicationDbContext db, [SlashParam(Required = false)] bool ShowInChannel = false) {
            await command.RespondAsync("Getting backups...", ephemeral: !ShowInChannel);
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var builder = new EmbedBuilder();
            builder.Title = $"Next Rank Details";
            foreach(var id in user.EggIncAccounts) {
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await ContractsAPI.FirstContact(id.Id)).Backup);
                var nextSubRank = SIPrefix.GetNextRankInfo(backup, true);

                var nextRankText = "";
                foreach(var subrank in nextSubRank.Take(5)) {
                    nextRankText += $"<:Egg_of_Prophecy_PE:669981330477547580>{subrank.EggsOfProphecy} <:Soul_Egg_SE:724341890794913964>{Math.Max(0, subrank.SoulsEggs).ToEggString()}\n";
                    if(subrank.SoulsEggs < 0)
                        break;
                }

                builder.AddField(new EmbedFieldBuilder { IsInline = true, Name = (user.EggIncAccounts.Count > 1 ? $"{backup.UserName}\n" : "") + $"{nextSubRank.First().Rank} [{nextSubRank.First().EarningsBonus.ToEggString()}]", Value = nextRankText });

                var nextRank = SIPrefix.GetNextRankInfo(backup, false);
                var currentRank = SIPrefix.GetPrefixFromEB(backup.EarningsBonus);
                if(nextRank.First().SoulsEggs != nextSubRank.First().SoulsEggs) {
                    nextRankText = "";
                    foreach(var subrank in nextRank.Take(5)) {
                        nextRankText += $"<:Egg_of_Prophecy_PE:669981330477547580>{subrank.EggsOfProphecy} <:Soul_Egg_SE:724341890794913964>{Math.Max(0, subrank.SoulsEggs).ToEggString()}\n";
                        if(subrank.SoulsEggs < 0)
                            break;
                    }

                    builder.AddField(new EmbedFieldBuilder { IsInline = true, Name = (user.EggIncAccounts.Count > 1 ? $"{backup.UserName}\n" : "") + $"{nextRank.First().Rank} [{nextRank.First().EarningsBonus.ToEggString()}]", Value = nextRankText });
                }

                var ge = backup.GoldenEggsEarned - backup.GoldenEggsSpent;
                builder.AddField(new EmbedFieldBuilder {
                    IsInline = false, Name = "Current Details", Value = @$"{currentRank.RankWithSubRank}
<:Egg_of_Prophecy_PE:669981330477547580>{backup.EggsOfProphecy}
<:Soul_Egg_SE:724341890794913964>{backup.SoulEggs.ToEggString(numberOfDecimalPlaces: 3)}
EB {backup.EarningsBonus.ToEggString(numberOfDecimalPlaces: 3)}
Prestiges {backup.NumPrestiges}
<:Soul_Egg_SE:724341890794913964>/Prestige {(backup.SoulEggs / backup.NumPrestiges).ToEggString(numberOfDecimalPlaces: 3)}
<:Golden_Egg_GE:692439755798872075> {(ge > 1_000_000_000 ? ge.ToEggString(numberOfDecimalPlaces: 3) : ge.ToString("n0"))}
<:Piggy_bank:724396277676113955>  {(backup.TotalGEInPiggyBank > 1_000_000_000 ? backup.TotalGEInPiggyBank.ToEggString(numberOfDecimalPlaces: 3) : backup.TotalGEInPiggyBank.ToString("n0"))}
<:Drone:755719353529270342> {backup.DroneTakedowns.ToString("n0")}
<:Drone:755719353529270342> Elite {backup.DroneTakedownsElite.ToString("n0")}
Last Backup <t:{backup.LastBackupTime}:R>
"
                });
            }

            //await command.Channel.SendMessageAsync($"{command.User.Mention} used the command `/nextrank`", embed: builder.Build());
            //await command.DeleteResponseFix();
            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Embed = builder.Build();
            });
        }

        [SlashCommand(Description = "Rename a co-op channel to mistype", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task RenameCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] string correctcoopname) {
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.RespondAsync($"⚠️ERROR: Command only works in co-op channels");
                return;
            }


            targetCoop.Name = correctcoopname;
            await db.SaveChangesAsync();
            await command.RespondAsync($"Co-op renamed to {correctcoopname}");
        }

        [SlashCommand(Description = "Trigger an update for a co-op or contract channel", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task UpdateChannel(FauxCommand command, ApplicationDbContext db, CoopStatusUpdater coopStatusUpdater, DiscordSocketClient discord, ContractUpdater contractUpdater, APILink apiLink) {
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop != null) {
                await command.RespondAsync("Updating coop...", ephemeral: true);
                var guild = discord.Guilds.First(x => x.Id == targetCoop.OverflowGuildId);
                var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == targetCoop.GuildId);
                await coopStatusUpdater.ProcessCoop(targetCoop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default, db);
                await command.ModifyOriginalResponseAsync(m => m.Content = "Co-op Updated");
                return;
            }

            var targetGuildContract = await db.GuildContracts.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetGuildContract != null) {
                await command.RespondAsync("Updating contract...", ephemeral: true);
                var guild = discord.Guilds.First(x => x.Id == targetGuildContract.GuildID);

                //var dbusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id && !x.TempDisabled).ToListAsync();
                //var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guild.Id);
                //var backups = await apiLink.GetUserBackups(dbusers, db);
                //var backups = dbusers.Where(x => x.Backups is not null).SelectMany(x => x.Backups.Where(y => x.EggIncAccounts.Any(eid => eid.Id == y.EggIncId)).Select(y => new LeaderboardUser { User = x, Backup = y })).ToList();

                await contractUpdater.UpdateContractChannel(db, targetGuildContract, guild, command);
                await command.ModifyOriginalResponseAsync(x => x.Content = "Content Updated");
                //await command.DeleteOriginalResponseAsync();
                return;
            }

            await command.RespondAsync($"⚠️ERROR: Command only works in contract or co-op channels");
        }

        [SlashCommand(Description = "Adds a temporary role for users that last a specific amount of time", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task TempRole(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] SocketRole role, [SlashParam] string timespan, [SlashParam] string reason, [SlashParam] SocketGuildUser[] users) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.Now);
            } catch(Exception ex) {
                await command.RespondAsync($"Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }

            await command.DeferAsync();
            var userids = users.Select(x => x.Id);
            var existingTempRoles = await db.TemporaryRoles.Where(x => x.RoleId == role.Id && x.Expires > DateTimeOffset.Now && userids.Contains(x.UserId)).ToListAsync();
            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            foreach(var user in users) {
                var tempRole = existingTempRoles.FirstOrDefault(x => x.RoleId == role.Id && user.Id == x.UserId);
                if(tempRole == null) {
                    tempRole = new TemporaryRole { RoleId = role.Id, Created = DateTimeOffset.Now, UserId = user.Id, GuildId = guild.Id };
                    db.Add(tempRole);
                    await user.AddRoleAsync(role);
                }

                tempRole.Reason = reason;
                tempRole.Expires = expireTime;
            }

            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(m => m.Content = $"Added the role {role.Emoji} {role.Name} to the following {"user".ToQuantity(users.Count(), ShowQuantityAs.None)} {string.Join(", ", users.Select(x => x.Mention))} until <t:{expireTime.ToUnixTimeSeconds()}:f> for the reason: {reason}");
        }

        [SlashCommand(Description = "Adds a temporary name to be used for co-op naming", AdminOnly = StaffOnlyLevel.CluckingCoordinator, ParentCommand = "a")]
        public static async Task TempCustomCoopName(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string customName, [SlashParam] string timespan, [SlashParam] SocketGuildUser user) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.Now);
            } catch(Exception ex) {
                await command.RespondAsync($"Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }

            await command.DeferAsync();
            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == user.Id);
            dbuser.CustomCoopName = customName;
            dbuser.ExpireCustomCoopName = expireTime;

            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(m => m.Content = $"Added the custom co-op prefix {customName} to {user.Mention} until <t:{expireTime.ToUnixTimeSeconds()}:f>");
        }

        [SlashCommand(Description = "Get help from staff, please give details")]
        public static async Task CallStaff(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string details, [SlashParam(Description = "If private then only staff will see your message", Required = false)] bool keepPrivate = false) {
            var guildFind = db.Guilds.First(x => x.Id == command.GuildId || x.OverflowServersJson.IndexOf(command.GuildId.ToString()) > -1);

            if(guildFind is null) {
                await command.RespondAsync("Callstaff cannot be sent, guild not found.");
                return;
            } else if(!guildFind.HasChannel(GuildChannelType.CallStaffChannel)) {
                await command.RespondAsync("Callstaff cannot be sent, CallStaffChannel is not set.");
                return;
            }

            var socketGuild = client.Guilds.First(x => x.Id == guildFind.Id);

            if(socketGuild is null) {
                await command.RespondAsync("Callstaff cannot be sent, SocketGuild could not be found via mapping.");
                return;
            }

            var channel = socketGuild.TextChannels.FirstOrDefault(x => x.Id == guildFind.ChannelDetails.FirstOrDefault(c => c.ChannelType == GuildChannelType.CallStaffChannel).Id);

            if(channel is null) {
                await command.RespondAsync("Callstaff cannot be sent, CallStaffChannel could not be found.");
                return;
            }

            var staffRole = socketGuild.Roles.FirstOrDefault(x => x.Id == guildFind.ChannelDetails.FirstOrDefault(c => c.ChannelType == GuildChannelType.CallStaffTagRole).Id);
            var staffTag = staffRole is null ? "" : $"<@&{staffRole.Id}>: ";

            var infoText = $"Staff has been called ({details})";

            await channel.SendMessageAsync($"{staffTag}{command.User.Mention}{(keepPrivate ? " **privately** " : " ")}called for staff in <#{command.Channel.Id}> with the details: {details}");
            await command.RespondAsync(infoText, ephemeral: keepPrivate);
            if(keepPrivate) {
                var dmChannel = await command.User.CreateDMChannelAsync();
                try {
                    var message = await dmChannel.SendMessageAsync(infoText);
                } catch(Exception) {
                    await command.Channel.SendMessageAsync($"Private callstaff sent. (DMs are blocked)");
                }
            }
        }

        [SlashCommand(Description = "Lookup brief explanations of key topics", AllowInDMs = true)]
        public static async Task FAQ(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam(Description = "Topic or keyword")] string query, [SlashParam(Description = "Show in channel", Required = false)] bool showInChannel = true) {
            var userRunning = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);

            if(userRunning is null) {
                await command.RespondAsync("Could not determine who you are ... (report this)", ephemeral: true);
            }

            var guildObj = db.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? db.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId);
            var socketGuild = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? client.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId); ;

            if(guildObj is null || socketGuild is null) {
                await command.RespondAsync("Could not determine which server you are a part of ... (report this)", ephemeral: true);
            }

            var faqTopics = _FaqTopics
                .Where(f => f.StaffOnlyLevel == StaffOnlyLevel.None)
                .Where(f => f.Keywords.Any(k => k.Contains(query)))
                .Where(f => f.ApplicableToGuild(guildObj, socketGuild))
                .OrderByDescending(f => f.Keywords.Count(f => f.Contains(query)))
                .ToList();

            if(faqTopics.Any()) {
                var builder = FAQEmbedBuilder(guildObj.Id.ToString(), faqTopics, faqTopics.First());
                await command.RespondAsync(components: builder.ComponentBuilder?.Build(), embed: builder.EmbedBuilder.Build(), ephemeral: !showInChannel);
            } else {
                await command.RespondAsync(content: $"Could not find any faq topics for the term `{query}`", ephemeral: true);
            }
        }

        [SlashCommand(Description = "Lookup brief explanations of key topics/templates", AllowInDMs = true, AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task FAQ(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, ILogger logger, [SlashParam(Description = "Topic or keyword")] string query, [SlashParam(Description = "Show in channel", Required = false)] bool showInChannel = true) {
            var userRunning = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);

            if(userRunning is null) {
                await command.RespondAsync("Could not determine who you are ... (report this)", ephemeral: true);
            }

            var guildObj = db.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? db.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId);
            var socketGuild = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId) ?? client.Guilds.FirstOrDefault(g => g.Id == userRunning.GuildId); ;

            if(guildObj is null || socketGuild is null) {
                await command.RespondAsync("Could not determine which server you are a part of ... (report this)", ephemeral: true);
            }

            var faqTopics = _FaqTopics
                .Where(f => f.Keywords.Any(k => k.Contains(query)))
                .Where(f => f.ApplicableToGuild(guildObj, socketGuild))
                .OrderByDescending(f => f.Keywords.Count(f => f.Contains(query)))
                .ToList();

            if(faqTopics.Any()) {
                var builder = FAQEmbedBuilder(guildObj.Id.ToString(), faqTopics, faqTopics.First());
                await command.RespondAsync(components: builder.ComponentBuilder?.Build(), embed: builder.EmbedBuilder.Build(), ephemeral: faqTopics.Any(f => f.StaffOnlyLevel != StaffOnlyLevel.None) || !showInChannel);
            } else {
                await command.RespondAsync(content: $"Could not find any faq topics for the term `{query}`", ephemeral: true);
            }
        }

        [ComponentCommand]
        public static async Task LoadFAQ(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {

            var currentItem = _FaqTopics.FirstOrDefault(f => f.Name == data.Split(",")[0]);
            var guildId = data.Split(",")[1];
            var items = data.Split(",")[2].Split("|").ToList().Select(item => _FaqTopics.FirstOrDefault(f => f.Name == item)).ToList();

            var builder = FAQEmbedBuilder(guildId, items, currentItem);
            await component.UpdateAsync(x => { x.Components = builder.ComponentBuilder?.Build(); x.Embed = builder.EmbedBuilder.Build(); });
        }

        public static FAQBuilder FAQEmbedBuilder(string guildId, List<FAQItem> items, FAQItem currentItem) {
            var builder = new FAQBuilder() {
                ComponentBuilder = null
            };

            var componentBuilder = new ComponentBuilder();
            var buttonCount = 0;

            var embedBuilder = new EmbedBuilder().WithAuthor(
                    new EmbedAuthorBuilder()
                        .WithName($"{currentItem.Name} (Click me for More Information)")
                        .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.png"))
                        .WithUrl($"https://egg9000.com/FAQ?guildId={guildId}&name={currentItem.Name}")
                .WithColor(currentItem.EmbedColor);
            embedBuilder.AddField("Explanation", currentItem.Explanation);

            var indexInList = items.IndexOf(currentItem);
            var itemsInList = items.Count;

            if(indexInList > 0 && itemsInList > 1 && items[indexInList - 1] is not null) {
                componentBuilder.WithButton($"← {items[indexInList - 1].Name}", $"LoadFAQ:{items[indexInList - 1].Name},{string.Join("|", items.Select(i => i.Name))}"); buttonCount++;
            }
            if(indexInList < items.Count - 1 && items[indexInList + 1] is not null) {
                componentBuilder.WithButton($"{items[indexInList + 1].Name} →", $"LoadFAQ:{items[indexInList + 1].Name},{string.Join("|", items.Select(i => i.Name))}"); buttonCount++;
            }
            if(buttonCount > 0) builder.ComponentBuilder = componentBuilder;

            builder.EmbedBuilder = embedBuilder;
            return builder;
        }
    }
}