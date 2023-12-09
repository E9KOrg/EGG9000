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
using static EGG9000.Bot.Commands.ContractCommandsSlash;
using System.ComponentModel;
using EGG9000.Bot.Automated.Coops;
using Microsoft.AspNetCore.Connections.Features;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using EGG9000.Bot.Common.Helpers;
using System.Threading.Channels;

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

            var builder = new EmbedBuilder {
                Title = $"EB Tracking"
            };
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

                if(backup.EarningsBonus == 0) {
                    builder.AddField("Error", "The API is not responding correctly.\nPlease try again later.", true);
                    logger.LogWarning("Error: TrackEB 0 EB detected for {username}", backup.UserName ?? id.Name ?? id.Id);
                } else {
                    builder.AddField("Current EB", $"{backup.EarningsBonus.ToEggString()}\n{DiscordHelpers.TimeStamper(backupDate, DiscordHelpers.DiscordTimestampFormat.Relative)}", true);

                    if(id.LastEBTime.HasValue) {
                        var change = backup.EarningsBonus - id.LastEB;
                        var percentChange = change / id.LastEB * 100d;

                        var format = Math.Abs(percentChange - Math.Round(percentChange)) < 0.01 ? "F0" : "F2";

                        var timeStampDifference = (id.LastEBTime.Value - backupDate).Humanize();
                        builder.AddField("EB Gained", $"{change.ToEggString()} (+{percentChange.ToString(format)}%)\n{timeStampDifference}", true);
                    } else {
                        builder.AddField("First Update", "No previous EB to compare to", true);
                    }

                    id.LastEB = backup.EarningsBonus;
                    id.LastEBTime = backupDate;
                }
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
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guild.Id);
                await contractUpdater.UpdateContractChannel(db, targetGuildContract, guild, dbguild, command);
                await command.ModifyOriginalResponseAsync(x => x.Content = "Content Updated");
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
        public static async Task CallStaff(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] string details, [SlashParam(Description = "If private then only staff will see your message", Required = false)] bool keepPrivate = false)
        {
            var guildFind = db.Guilds.First(x => x.Id == command.GuildId || x.OverflowServersJson.IndexOf(command.GuildId.ToString()) > -1);

            if(guildFind is null) {
                await command.RespondAsync("Callstaff cannot be sent, guild not found.");
                return;
            } else if(!guildFind.HasChannel(GuildChannelType.CallStaffChannel)) {
                await command.RespondAsync("Callstaff cannot be sent, CallStaffChannel is not set.");
                return;
            }

            var socketGuild = _client.Guilds.First(x => x.Id == guildFind.Id);

            if(socketGuild is null) {
                await command.RespondAsync("Callstaff cannot be sent, SocketGuild could not be found via mapping.");
                return;
            }

            var staffRole = socketGuild.Roles.FirstOrDefault(x => x.Id == guildFind.ChannelDetails.FirstOrDefault(c => c.ChannelType == GuildChannelType.CallStaffTagRole).Id);
            var staffTag = staffRole is null ? "" : $"<@&{staffRole.Id}>: ";
            var infoText = $"Staff has been called ({details})";
            var message = $"{staffTag}{command.User.Mention}{(keepPrivate ? " **privately** " : " ")}called for staff in <#{command.Channel.Id}> with the details: {details}";

            var response = await ChannelHelper.DetermineAndSend(db, _client, guildFind, socketGuild, GuildChannelType.CallStaffChannel, new() { Text = message });

            if(response is null) {
                await command.RespondAsync("Callstaff cannot be sent, CallStaffChannel could not be found.");
                return;
            }

            await command.RespondAsync(infoText, ephemeral: keepPrivate);
            if(keepPrivate) {
                var dmChannel = await command.User.CreateDMChannelAsync();
                var retEx = await DiscordHelpersExt.BoolSendDm(dmChannel, infoText);
                var dbUser = db.DBUsers.FirstOrDefault(u => u.DiscordId == command.User.Id);
                if(dbUser is not null && (retEx == null) == dbUser.DMSBlocked) {
                    dbUser.DMSBlocked = !dbUser.DMSBlocked;
                    await db.SaveChangesAsync();
                }
                if(retEx != null) await command.Channel.SendMessageAsync($"Private callstaff sent. (DMs are blocked)");
            }
        }
    }
}
