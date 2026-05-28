using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.DiscordHelpersExt;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Commands {
    public class MiscModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient client, ThreadsCoopStatusUpdater coopStatusUpdaterThreads, ContractUpdater contractUpdater, ILogger<MiscModule> logger) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {
        private readonly DiscordSocketClient _client = client;
        private readonly ThreadsCoopStatusUpdater _coopStatusUpdaterThreads = coopStatusUpdaterThreads;
        private readonly ContractUpdater _contractUpdater = contractUpdater;
        private readonly ILogger<MiscModule> _logger = logger;

        [SlashCommand("starttestprocess", "Start EID screenshot recognition process.")]
        [EnabledInDm(true)]
        public async Task StartTestProcess() {
            if(Context.Interaction.GuildId != null) {
                await Context.Interaction.RespondAsyncGettingMessage(
                    "",
                    embed: EmbedError("This command should **only** be used in DMs.")
                );
                return;
            }

            await Context.Interaction.RespondAsyncGettingMessage(
                "",
                embed: EmbedSuccess("Please reply (a real Discord Reply - hit the Reply button) to this message with an **uncropped screenshot of your Privacy & Data tab**.")
            );
        }

        [SlashCommand("trackeb", "Track your EB since the last time you ran this command")]
        [EnabledInDm(true)]
        public async Task TrackEB() {
            await Context.Interaction.DeferAsync(ephemeral: Context.Interaction.IsDMInteraction ? false : true);
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(dbUser == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{Context.User.Id}>.\nAre you registered?"); });
                return;
            }

            var builder = new EmbedBuilder {
                Title = $"EB Tracking"
            };
            foreach(var id in dbUser.EggIncAccounts) {
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await EggIncApi.FirstContact(id.Id)).Backup, await db.CachedEiContractsAsync(), backup);
                if(dbUser.EggIncAccounts.Count > 1) {
                    builder.AddField("――――――――――――――――――", $"**{backup.UserName}**");
                }

                var backupDate = DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime);

                if(id.LastEBTime.HasValue) {
                    builder.AddField("Last EB", $"{id.LastEB.ToEggString()}\n{DiscordHelpers.TimeStamper(id.LastEBTime.Value, DiscordHelpers.DiscordTimestampFormat.Relative)}", true);
                }

                if(backup.EarningsBonus == 0) {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("The API is not responding correctly.\nPlease try again later."); });
                    _logger.LogWarning("Warning: TrackEB 0 EB detected for {username}", backup.UserName ?? id.Name ?? id.Id);
                    return;
                } else {
                    builder.AddField("Current EB", $"{backup.EarningsBonus.ToEggString()}\n{DiscordHelpers.TimeStamper(backupDate, DiscordHelpers.DiscordTimestampFormat.Relative)}", true);

                    if(id.LastEBTime.HasValue) {
                        var change = backup.EarningsBonus - id.LastEB;
                        var percentChange = change / id.LastEB * 100d;

                        var format = Math.Abs(percentChange - Math.Round(percentChange)) < 0.01 ? "F0" : "F2";

                        var timeStampDifference = (id.LastEBTime.Value - backupDate).Humanize();
                        builder.AddField("EB Gained", $"{change.ToEggString()} ({(percentChange > 0 ? "+" : "")}{percentChange.ToString(format)}%)\n{timeStampDifference}", true);
                    } else {
                        builder.AddField("First Update", "No previous EB to compare to", true);
                    }

                    id.LastEB = backup.EarningsBonus;
                    id.LastEBTime = backupDate;
                }
            }

            dbUser.UpdateAccounts();
            await Db.SaveChangesAsync();
            await Context.Interaction.ModifyOriginalResponseAsync(x => {
                x.Embed = builder.Build();
                x.Content = "";
            });
        }

        [SlashCommand("nextrank", "How many SE/PE needed for next rank up")]
        [EnabledInDm(true)]
        public async Task NextRank([Summary("ShowInChannel")] bool ShowInChannel = false) {
            await Context.Interaction.DeferAsync(ephemeral: !ShowInChannel);
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id);
            if(dbUser == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{Context.User.Id}>.\nAre you registered?"); });
                return;
            }

            var builder = new EmbedBuilder() {
                Title = "Next Rank Details"
            };
            foreach(var id in dbUser.EggIncAccounts) {
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await EggIncApi.FirstContact(id.Id)).Backup, await db.CachedEiContractsAsync(), backup);
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

            await Context.Interaction.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Embed = builder.Build();
            });
        }

        [SlashCommand("renamecoop", "Rename a co-op channel to mistype")]
        [DefaultMemberPermissions(Discord.GuildPermission.CreatePrivateThreads)]
        public async Task RenameCoop([Summary("correctcoopname")] string correctcoopname) {
            await Context.Interaction.DeferAsync();
            var targetCoop = await Db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(targetCoop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => x.Embed = EmbedError($"Command only works in co-op channels"));
                return;
            }


            targetCoop.Name = correctcoopname;
            await Db.SaveChangesAsync();
            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"Co-op renamed to {correctcoopname}");
        }

        [SlashCommand("updatechannel", "Trigger an update for a co-op or contract channel")]
        [DefaultMemberPermissions(Discord.GuildPermission.ManageChannels)]
        public async Task UpdateChannel() {
            var command = Context.Interaction;
            await command.DeferAsync(ephemeral: true);
            var targetCoop = await Db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(targetCoop != null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = "Updating coop...");
                var guild = _client.Guilds.First(x => x.Id == targetCoop.OverflowGuildId);
                var users = await Db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
                var dbguild = await Db.Guilds.AsQueryable().FirstAsync(x => x.Id == targetCoop.GuildId);
                var parentGuild = _client.Guilds.First(x => x.Id == dbguild.Id);
                await _coopStatusUpdaterThreads.ProcessCoop(targetCoop.Id, guild, parentGuild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default);

                await command.ModifyOriginalResponseAsync(m => m.Content = "Co-op Updated");
                return;
            }

            var targetGuildContract = await Db.GuildContracts.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetGuildContract != null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = "Updating contract...");
                var guild = _client.Guilds.First(x => x.Id == targetGuildContract.GuildID);
                var dbguild = await Db.Guilds.AsQueryable().FirstAsync(x => x.Id == guild.Id);
                await _contractUpdater.UpdateContractChannel(Db, targetGuildContract, guild, dbguild, command);
                await command.ModifyOriginalResponseAsync(x => x.Content = "Content Updated");
                return;
            }

            await command.ModifyOriginalResponseAsync(x => x.Embed = EmbedError($"Command only works in contract or co-op channels"));
        }

        [SlashCommand("temprole", "Adds a temporary role for users that last a specific amount of time")]
        [DefaultMemberPermissions(Discord.GuildPermission.ManageChannels)]
        public async Task TempRole(
            [Summary("role")] SocketRole role,
            [Summary("timespan")] string timespan,
            [Summary("reason")] string reason,
            [Summary("user1", "User 1")] SocketGuildUser user1,
            [Summary("user2", "User 2")] SocketGuildUser user2 = null,
            [Summary("user3", "User 3")] SocketGuildUser user3 = null,
            [Summary("user4", "User 4")] SocketGuildUser user4 = null,
            [Summary("user5", "User 5")] SocketGuildUser user5 = null,
            [Summary("user6", "User 6")] SocketGuildUser user6 = null,
            [Summary("user7", "User 7")] SocketGuildUser user7 = null,
            [Summary("user8", "User 8")] SocketGuildUser user8 = null,
            [Summary("user9", "User 9")] SocketGuildUser user9 = null,
            [Summary("user10", "User 10")] SocketGuildUser user10 = null
            ) {
            var users = EGG9000.Bot.Interactions.UserParams.CoalesceGuildUsers(user1, user2, user3, user4, user5, user6, user7, user8, user9, user10);
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.UtcNow);
            } catch(Exception ex) {
                await Context.Interaction.RespondAsyncGettingMessage($"Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }

            var maxRolePosition = ((SocketGuildUser)Context.User).Roles.Max(role => role.Position);
            if(role.Position >= maxRolePosition) {
                await Context.Interaction.RespondAsyncGettingMessage("You cannot assign roles higher or equal than your own");
                return;
            }

            await Context.Interaction.DeferAsync();
            var userids = users.Select(x => x.Id);
            var existingTempRoles = await Db.TemporaryRoles.Where(x => x.RoleId == role.Id && x.Expires > DateTimeOffset.UtcNow && userids.Contains(x.UserId)).ToListAsync();
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == Context.Channel.Id));
            foreach(var user in users) {
                var tempRole = existingTempRoles.FirstOrDefault(x => x.RoleId == role.Id && user.Id == x.UserId);
                if(tempRole == null) {
                    tempRole = new TemporaryRole { RoleId = role.Id, Created = DateTimeOffset.UtcNow, UserId = user.Id, GuildId = guild.Id };
                    Db.Add(tempRole);
                    await user.AddRoleAsync(role);
                }

                tempRole.Reason = reason;
                tempRole.Expires = expireTime;
            }

            await Db.SaveChangesAsync();

            await Context.Interaction.ModifyOriginalResponseAsync(m => m.Content = $"Added the role {role.Emoji} {role.Name} to the following {"user".ToQuantity(users.Length, ShowQuantityAs.None)} {string.Join(", ", users.Select(x => x.Mention))} until <t:{expireTime.ToUnixTimeSeconds()}:f> for the reason: {reason}");
        }

        [ComponentInteraction("WhatIsRSC", ignoreGroupNames: true)]
        public async Task WhatIsRSC() {
            var component = (SocketMessageComponent)Context.Interaction;
            var rscText = "The Contract Eggspert role is awarded to the top 10 highest scoring players of each scored contract, as well as the top-performers in Grades C, B, and A. The role will be removed after 7 days, and serves only to recognize eggceptional performance.\n\n" +
                "Score is determined by comparing a player's `Total Eggs Delivered` to the 50 closest players in EB (25 above, 25 below). The number (score) next to a name denotes how many times greater than average the user's total was. I.e., a score of 1 is average, a score of 2 is double the average, etc.\n\n" +
                "Contracts are scored manually by Palace staff once all Palace coops have finished, and the contract has expired. You can read more about scoring, and Running Score, in this announcement: https://discord.com/channels/656455567858073601/698270110279925770/939264092445745163";
            var rscEmbed = new EmbedBuilder().WithColor(Color.LighterGrey).WithDescription(rscText).WithAuthor(new EmbedAuthorBuilder().WithName("What is this?").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();

            await component.RespondAsync(text: "", embed: rscEmbed, ephemeral: true);
        }

        [SlashCommand("callstaff", "Get help from staff, please give details")]
        public async Task CallStaff([Summary("details")] string details, [Summary("keepPrivate", "If private then only staff will see your message")] bool keepPrivate = false) {
            await Context.Interaction.DeferAsync(ephemeral: keepPrivate);
            var guildFind = Db.Guilds.First(x => x.Id == Context.Interaction.GuildId || x.OverflowServersJson.IndexOf(Context.Interaction.GuildId.ToString()) > -1);

            if(guildFind is null) {
                await Context.Interaction.ModifyOriginalResponseAsync("Callstaff cannot be sent, guild not found.");
                return;
            } else if(!guildFind.HasChannel(GuildChannelType.CallStaffChannel)) {
                await Context.Interaction.ModifyOriginalResponseAsync("Callstaff cannot be sent, CallStaffChannel is not set.");
                return;
            }

            var socketGuild = _client.Guilds.First(x => x.Id == guildFind.Id);

            if(socketGuild is null) {
                await Context.Interaction.ModifyOriginalResponseAsync("Callstaff cannot be sent, SocketGuild could not be found via mapping.");
                return;
            }

            var staffRole = socketGuild.Roles.FirstOrDefault(x => x.Id == guildFind.ChannelDetails.FirstOrDefault(c => c.ChannelType == GuildChannelType.CallStaffTagRole).Id);
            var staffTag = staffRole is null ? "" : $"<@&{staffRole.Id}>: ";
            var infoText = $"Staff has been called ({details})";
            var message = $"{Context.User.Mention}{(keepPrivate ? " **privately** " : " ")}called for staff in <#{Context.Channel.Id}> with the details: {details}";

            if(keepPrivate) {
                var channelForThreads = await ChannelHelper.GetTextChannel(Db, _client, guildFind, socketGuild, GuildChannelType.PrivateCallStaff);
                if(channelForThreads is not null) {
                    var thread = await channelForThreads.CreateThreadAsync(name: $"{Context.User.GlobalName ?? Context.User.Username} [callstaff]", type: ThreadType.PrivateThread, invitable: false);
                    var messageToPing = await thread.SendMessageAsync(".");
                    await messageToPing.ModifyAsync(x => x.Content = staffTag);
                    await messageToPing.DeleteAsync();
                    await thread.SendMessageAsync(text: $"{Context.User.Mention}", embed: EmbedCustom(Color.DarkerGrey, "CallStaff", message));

                    var response = await ChannelHelper.DetermineAndSend(_client, guildFind, GuildChannelType.CallStaffChannel, new() { Text = staffTag + message + " " + thread.Mention });

                    await Context.Interaction.ModifyOriginalResponseAsync($"{infoText}, they should respond in {thread.Mention}");

                    return;
                }
            }

            {
                var response = await ChannelHelper.DetermineAndSend(_client, guildFind, GuildChannelType.CallStaffChannel, new() { Text = staffTag + message });

                if(response is null) {
                    await Context.Interaction.ModifyOriginalResponseAsync("Callstaff cannot be sent, CallStaffChannel could not be found.");
                    return;
                }

                await Context.Interaction.ModifyOriginalResponseAsync(infoText);

                if(keepPrivate) {
                    var dmResult = await BoolSendDm(Context.User, infoText, Db);
                    if(dmResult != DMResult.Success) await Context.Channel.SendMessageAsync($"Private callstaff sent. {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}");
                }
            }
        }
    }

    public partial class AdminModule {
        [Discord.Interactions.SlashCommand("tempcustomcoopname", "Adds a temporary name to be used for co-op naming")]
        public async Task TempCustomCoopName([Discord.Interactions.Summary("customname")] string customName, [Discord.Interactions.Summary("timespan")] string timespan, [Discord.Interactions.Summary("user")] SocketGuildUser user) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.UtcNow);
            } catch(Exception ex) {
                await Context.Interaction.RespondAsync($"Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }

            await Context.Interaction.DeferAsync();
            var guild = gateway.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == Context.Channel.Id));
            var dbuser = await Db.DBUsers.FirstAsync(x => x.DiscordId == user.Id);
            dbuser.CustomCoopName = customName;
            dbuser.ExpireCustomCoopName = expireTime;

            await Db.SaveChangesAsync();

            await Context.Interaction.ModifyOriginalResponseAsync(m => m.Content = $"Added the custom co-op prefix {customName} to {user.Mention} until <t:{expireTime.ToUnixTimeSeconds()}:f>");
        }
    }
}
