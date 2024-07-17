using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Microsoft.Extensions.Logging;

using Humanizer;

using Microsoft.EntityFrameworkCore;

using System;
using System.Linq;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.DiscordHelpersExt;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Commands {
    public static class MiscCommandsSlash {
        [SlashCommand(Description = "Track your EB since the last time you ran this command", AllowInDMs = true)]
        public static async Task TrackEB(FauxCommand command, ApplicationDbContext db, ILogger logger) {
            await command.DeferAsync();
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            }

            var builder = new EmbedBuilder {
                Title = $"EB Tracking"
            };
            foreach(var id in dbUser.EggIncAccounts) {
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await ContractsAPI.FirstContact(id.Id)).Backup, backup);
                if(dbUser.EggIncAccounts.Count > 1) {
                    builder.AddField("――――――――――――――――――", $"**{backup.UserName}**");
                }

                var backupDate = DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime);

                if(id.LastEBTime.HasValue) {
                    builder.AddField("Last EB", $"{id.LastEB.ToEggString()}\n{DiscordHelpers.TimeStamper(id.LastEBTime.Value, DiscordHelpers.DiscordTimestampFormat.Relative)}", true);
                }

                if(backup.EarningsBonus == 0) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("The API is not responding correctly.\nPlease try again later."); });
                    logger.LogWarning("Warning: TrackEB 0 EB detected for {username}", backup.UserName ?? id.Name ?? id.Id);
                    return;
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

            dbUser.UpdateAccounts();
            await db.SaveChangesAsync();
            await command.ModifyOriginalResponseAsync(x => {
                x.Embed = builder.Build();
                x.Content = "";
            });
        }

        [SlashCommand(Description = "How many SE/PE needed for next rank up", AllowInDMs = true)]
        public static async Task NextRank(FauxCommand command, ApplicationDbContext db, [SlashParam(Required = false)] bool ShowInChannel = false) {
            await command.DeferAsync(ephemeral: !ShowInChannel);
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            }

            var builder = new EmbedBuilder() {
                Title = "Next Rank Details"
            };
            foreach(var id in dbUser.EggIncAccounts) {
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await ContractsAPI.FirstContact(id.Id)).Backup, backup);
                var nextSubRank = SIPrefix.GetNextRankInfo(backup, true);

                var nextRankText = "";
                foreach(var subrank in nextSubRank.Take(5)) {
                    nextRankText += $"<:Egg_of_Prophecy_PE:669981330477547580>{subrank.EggsOfProphecy} <:Soul_Egg_SE:724341890794913964>{Math.Max(0, subrank.SoulsEggs).ToEggString()}\n";
                    if(subrank.SoulsEggs < 0)
                        break;
                }

                builder.AddField(new EmbedFieldBuilder { IsInline = true, Name = (dbUser.EggIncAccounts.Count > 1 ? $"{backup.UserName}\n" : "") + $"{nextSubRank.First().Rank} [{nextSubRank.First().EarningsBonus.ToEggString()}]", Value = nextRankText });

                var nextRank = SIPrefix.GetNextRankInfo(backup, false);
                var currentRank = SIPrefix.GetPrefixFromEB(backup.EarningsBonus);
                if(nextRank.First().SoulsEggs != nextSubRank.First().SoulsEggs) {
                    nextRankText = "";
                    foreach(var subrank in nextRank.Take(5)) {
                        nextRankText += $"<:Egg_of_Prophecy_PE:669981330477547580>{subrank.EggsOfProphecy} <:Soul_Egg_SE:724341890794913964>{Math.Max(0, subrank.SoulsEggs).ToEggString()}\n";
                        if(subrank.SoulsEggs < 0)
                            break;
                    }

                    builder.AddField(new EmbedFieldBuilder { IsInline = true, Name = (dbUser.EggIncAccounts.Count > 1 ? $"{backup.UserName}\n" : "") + $"{nextRank.First().Rank} [{nextRank.First().EarningsBonus.ToEggString()}]", Value = nextRankText });
                }

                var ge = backup.GoldenEggsEarned - backup.GoldenEggsSpent;
                builder.AddField(new EmbedFieldBuilder {
                    IsInline = false,
                    Name = "Current Details",
                    Value =
                        @$"{currentRank.RankWithSubRank}
                        <:Egg_of_Prophecy_PE:669981330477547580>{backup.EggsOfProphecy}
                        <:Soul_Egg_SE:724341890794913964>{backup.SoulEggs.ToEggString(numberOfDecimalPlaces: 3)}
                        EB {backup.EarningsBonus.ToEggString(numberOfDecimalPlaces: 3)}
                        Prestiges {backup.NumPrestiges}
                        <:Soul_Egg_SE:724341890794913964>/Prestige {(backup.SoulEggs / backup.NumPrestiges).ToEggString(numberOfDecimalPlaces: 3)}
                        <:Golden_Egg_GE:692439755798872075> {(ge > 1_000_000_000 ? ge.ToEggString(numberOfDecimalPlaces: 3) : ge.ToString("n0"))}
                        <:Piggy_bank:724396277676113955>  {(backup.TotalGEInPiggyBank > 1_000_000_000 ? backup.TotalGEInPiggyBank.ToEggString(numberOfDecimalPlaces: 3) : backup.TotalGEInPiggyBank.ToString("n0"))}
                        <:Drone:755719353529270342> {backup.DroneTakedowns:n0}
                        <:Drone:755719353529270342> Elite {backup.DroneTakedownsElite:n0}
                        Last Backup <t:{backup.LastBackupTime}:R>"
                });
            }

            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Embed = builder.Build();
            });
        }

        [SlashCommand(Description = "Rename a co-op channel to mistype", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task RenameCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] string correctcoopname) {
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Command only works in co-op channels"));
                return;
            }


            targetCoop.Name = correctcoopname;
            await db.SaveChangesAsync();
            await command.RespondAsync($"Co-op renamed to {correctcoopname}");
        }

        [SlashCommand(Description = "Trigger an update for a co-op or contract channel", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task UpdateChannel(FauxCommand command, ApplicationDbContext db, CoopStatusUpdater coopStatusUpdater, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, DiscordSocketClient discord, ContractUpdater contractUpdater) {
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(targetCoop != null) {
                await command.RespondAsync("Updating coop...", ephemeral: true);
                var guild = discord.Guilds.First(x => x.Id == targetCoop.OverflowGuildId);
                var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == targetCoop.GuildId);
                if(targetCoop.ThreadID != 0) {
                    var slashCommands = (await guild.GetApplicationCommandsAsync()).ToList().Where(c => c.Type == ApplicationCommandType.Slash).ToList();
                    await coopStatusUpdaterThreads.ProcessCoop(targetCoop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, slashCommands, default);
                } else if(targetCoop.DiscordChannelId != 0) {
                    await coopStatusUpdater.ProcessCoop(targetCoop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default);
                }

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

            await command.RespondAsync(content: "", embed: EmbedError($"Command only works in contract or co-op channels"));
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

            await command.ModifyOriginalResponseAsync(m => m.Content = $"Added the role {role.Emoji} {role.Name} to the following {"user".ToQuantity(users.Length, ShowQuantityAs.None)} {string.Join(", ", users.Select(x => x.Mention))} until <t:{expireTime.ToUnixTimeSeconds()}:f> for the reason: {reason}");
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

        [ComponentCommand]
        public static async Task WhatIsRSC(SocketMessageComponent component) {
            var rscText = "The Contract Eggspert role is awarded to the top 10 highest scoring players of each scored contract, as well as the top-performers in Grades C, B, and A. The role will be removed after 7 days, and serves only to recognize eggceptional performance.\n\n" +
                "Score is determined by comparing a player's `Total Eggs Delivered` to the 100 closest players in EB (50 above, 50 below). The number (score) next to a name denotes how many times greater than average the user's total was. I.e., a score of 1 is average, a score of 2 is double the average, etc.\n\n" +
                "Contracts are scored manually by Palace staff once all Palace coops have finished, and the contract has expired. You can read more about scoring, and Running Score, in this announcement: https://discord.com/channels/656455567858073601/698270110279925770/939264092445745163";
            var rscEmbed = new EmbedBuilder().WithColor(Color.LighterGrey).WithDescription(rscText).WithAuthor(new EmbedAuthorBuilder().WithName("What is this?").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();

            await component.RespondAsync(text: "", embed: rscEmbed, ephemeral: true);
        }

        [SlashCommand(Description = "Get help from staff, please give details")]
        public static async Task CallStaff(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] string details, [SlashParam(Description = "If private then only staff will see your message", Required = false)] bool keepPrivate = false) {
            await command.DeferAsync(ephemeral: keepPrivate);
            var guildFind = db.Guilds.First(x => x.Id == command.GuildId || x.OverflowServersJson.IndexOf(command.GuildId.ToString()) > -1);

            if(guildFind is null) {
                await command.ModifyOriginalResponseAsync("Callstaff cannot be sent, guild not found.");
                return;
            } else if(!guildFind.HasChannel(GuildChannelType.CallStaffChannel)) {
                await command.ModifyOriginalResponseAsync("Callstaff cannot be sent, CallStaffChannel is not set.");
                return;
            }

            var socketGuild = _client.Guilds.First(x => x.Id == guildFind.Id);

            if(socketGuild is null) {
                await command.ModifyOriginalResponseAsync("Callstaff cannot be sent, SocketGuild could not be found via mapping.");
                return;
            }

            var staffRole = socketGuild.Roles.FirstOrDefault(x => x.Id == guildFind.ChannelDetails.FirstOrDefault(c => c.ChannelType == GuildChannelType.CallStaffTagRole).Id);
            var staffTag = staffRole is null ? "" : $"<@&{staffRole.Id}>: ";
            var infoText = $"Staff has been called ({details})";
            var message = $"{command.User.Mention}{(keepPrivate ? " **privately** " : " ")}called for staff in <#{command.Channel.Id}> with the details: {details}";

            if(keepPrivate) {
                var channelForThreads = await ChannelHelper.GetTextChannel(db, _client, guildFind, socketGuild, GuildChannelType.PrivateCallStaff);
                if(channelForThreads is not null) {
                    var thread = await channelForThreads.CreateThreadAsync(name: $"{command.User.GlobalName ?? command.User.Username} [callstaff]", type: ThreadType.PrivateThread);
                    var messageToPing = await thread.SendMessageAsync(".");
                    await messageToPing.ModifyAsync(x => x.Content = staffTag);
                    await messageToPing.DeleteAsync();
                    await thread.SendMessageAsync(message);

                    var response = await ChannelHelper.DetermineAndSend(db, _client, guildFind, socketGuild, GuildChannelType.CallStaffChannel, new() { Text = message + " " + thread.Mention });

                    await command.ModifyOriginalResponseAsync($"{infoText}, they should respond in {thread.Mention}");

                    return;
                }
            }

            {
                var response = await ChannelHelper.DetermineAndSend(db, _client, guildFind, socketGuild, GuildChannelType.CallStaffChannel, new() { Text = staffTag + message });

                if(response is null) {
                    await command.ModifyOriginalResponseAsync("Callstaff cannot be sent, CallStaffChannel could not be found.");
                    return;
                }

                await command.ModifyOriginalResponseAsync(infoText);

                if(keepPrivate) {
                    var dmResult = await BoolSendDm(command.User, infoText, db);
                    if(dmResult != DMResult.Success) await command.Channel.SendMessageAsync($"Private callstaff sent. {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                }
            }
        }
    }
}
