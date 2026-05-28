using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    [Group("b", "Ban management commands")]
    [DefaultMemberPermissions(Discord.GuildPermission.ManageChannels)]
    public class BanGroupModule : EGG9000.Bot.Interactions.E9KModuleBase {
        private readonly DiscordHostedService _client;

        public BanGroupModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client) : base(dbFactory) {
            _client = client;
        }

        [SlashCommand("banlist", "Check the list of Users/EIDs that have been banned from the server via /kick")]
        public async Task BanList() {
            await Context.Interaction.DeferAsync();
            var guildId = (await Db.Guilds.FirstOrDefaultAsync(g => g.Id == Context.Interaction.GuildId || g.OverflowServersJson.Contains(Context.Interaction.GuildId.ToString())))?.Id ?? ulong.MaxValue;
            var bannedUsers = await Db.DBUsers.Where(u => (u.Banned && (u.LastGuild == guildId || u.GuildId == guildId)) || (u.ServersBannedFrom != null && u.ServersBannedFrom.IndexOf(guildId.ToString()) > -1)).ToListAsync();
            if(bannedUsers is null || bannedUsers.Count == 0) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("No users are banned from this guild."); });
                return;
            }
            var userList = string.Join("\n", bannedUsers.Select(u => $"{u.DiscordUsername}\t{u.DiscordId}\t" + string.Join(", ", u.EggIncAccounts.Select(a => a.Id).ToList()))) ?? "Could not compile list";
            var guildName = (await Db.Guilds.FirstOrDefaultAsync(x => x.Id == Context.Interaction.GuildId)).Name;

            var responseEmbedBuilder = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder().WithName("Banned Users").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).WithColor(Color.DarkRed)
                        .WithDescription($"Users Banned from {guildName}\n\n" +
                        $"{(userList.Length > 1600 ? "_(List too large for Discord - see attached file)_\n" : userList)}");

            if(userList.Length > 1600) await Context.Interaction.RespondWithFilesAsyncGettingMessage([new FileAttachment(new MemoryStream(Encoding.UTF8.GetBytes(userList.Replace("<@", "").Replace(">", ""))), "BannedUsers.txt")], text: "", embed: responseEmbedBuilder.Build());
            else await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: responseEmbedBuilder.Build());
        }

        [SlashCommand("removeban", "Remove the ban placed on a user, and their associated EID(s)")]
        public async Task RemoveBan([Summary("user", "Discord ID of user to unban")] SocketUser user) {
            await Context.Interaction.DeferAsync();
            var dbBanMessage = "";
            var dbuser = Db.DBUsers.FirstOrDefault(u => u.DiscordId == user.Id);
            if(dbuser is not null && dbuser.Banned) {
                var dbGuild = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == Context.Interaction.GuildId || g.OverflowServersJson.Contains(Context.Interaction.GuildId.ToString()));
                var bannedServersList = dbuser.ServersBannedFrom?.Split(",").ToList() ?? [];
                var wasDbBanned = bannedServersList.Contains(dbGuild.Id.ToString());
                if(wasDbBanned) {
                    bannedServersList.Remove(dbGuild.Id.ToString());
                    dbuser.ServersBannedFrom = string.Join(",", bannedServersList);
                }
                dbuser.Banned = false;
                await Db.SaveChangesAsync();
                dbBanMessage = "User's DB ban was removed";
            } else {
                dbBanMessage = "No banned DBUser entry found for this user.";
            }

            var discordBanMessage = "";
            var socketGuild = _client.GetGuild(Context.Interaction.GuildId ?? ulong.MaxValue);
            var targetUser = socketGuild.GetUser(user.Id) ?? await _client.Gateway.GetUserAsync(user.Id);
            var runningUser = socketGuild?.Users?.ToList().FirstOrDefault(u => u.Id == Context.User.Id);
            if(runningUser is not null && runningUser.GuildPermissions.ToList().Contains(GuildPermission.BanMembers)) {
                var banObject = await socketGuild.GetBanAsync(targetUser);
                if(banObject is null) {
                    discordBanMessage = "User is not banned from via Discord.";
                } else {
                    await socketGuild.RemoveBanAsync(targetUser);
                    discordBanMessage = "User has been unbanned via Discord.";
                }
            } else {
                discordBanMessage = "You do not have the `BanMembers` permission.";
            }

            var unbanEmbed = new EmbedBuilder().WithColor(Color.LighterGrey)
                .AddField("Database Ban Status", dbBanMessage)
                .AddField("Discord Ban Status", discordBanMessage)
                .WithAuthor(new EmbedAuthorBuilder().WithName("Ban Status")
                .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp"))
            .Build();

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = unbanEmbed; });
        }
    }

    public class BanModule : EGG9000.Bot.Interactions.E9KModuleBase {
        private readonly DiscordHostedService _client;

        public BanModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client) : base(dbFactory) {
            _client = client;
        }

        [SlashCommand("kick", "Kick user with dm")]
        [DefaultMemberPermissions(Discord.GuildPermission.Administrator | Discord.GuildPermission.ManageChannels | Discord.GuildPermission.ManageRoles)]
        public async Task Kick([Summary("user1", "User to kick 1")] SocketUser user1, [Summary("reason", "reason")] string reason, [Summary("banaccount", "banaccount")] bool banaccount = false, [Summary("user2", "User to kick 2")] SocketUser user2 = null, [Summary("user3", "User to kick 3")] SocketUser user3 = null, [Summary("user4", "User to kick 4")] SocketUser user4 = null, [Summary("user5", "User to kick 5")] SocketUser user5 = null, [Summary("user6", "User to kick 6")] SocketUser user6 = null, [Summary("user7", "User to kick 7")] SocketUser user7 = null, [Summary("user8", "User to kick 8")] SocketUser user8 = null, [Summary("user9", "User to kick 9")] SocketUser user9 = null, [Summary("user10", "User to kick 10")] SocketUser user10 = null) {
            await Context.Interaction.DeferAsync();
            var users = EGG9000.Bot.Interactions.UserParams.CoalesceUsers(user1, user2, user3, user4, user5, user6, user7, user8, user9, user10);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == Context.Channel.Id));
            var dbGuild = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == Context.Interaction.GuildId || g.OverflowServersJson.Contains(Context.Interaction.GuildId.ToString()));

            var kicklist = new List<ulong>();
            var exceptionList = new List<ulong>();
            foreach(var targetUser in users) {
                var kickedWithoutDm = false;
                var dmChannel = await targetUser.CreateDMChannelAsync();

                if(banaccount && await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id) is { } dbUser) {
                    var bannedServersList = dbUser.ServersBannedFrom?.Split(",")?.ToList() ?? [];
                    bannedServersList.Add(dbGuild.Id.ToString());
                    dbUser.ServersBannedFrom = string.Join(",", bannedServersList);
                    dbUser.Banned = true;
                    await Db.SaveChangesAsync();
                }

                try {
                    var verbiage = banaccount ? "banned" : "kicked";
                    await dmChannel.SendMessageAsync($"You have been {verbiage} from {guild.Name} for the reason: {reason}.");
                } catch(HttpException) {
                    kickedWithoutDm = true;
                }

                var runningUser = _client.Guilds?.FirstOrDefault(g => g.Id == Context.Interaction.GuildId)?.Users?.ToList().FirstOrDefault(u => u.Id == Context.User.Id);
                var canBan = banaccount && runningUser is not null && runningUser.GuildPermissions.ToList().Contains(GuildPermission.BanMembers);

                try {
                    var execDiscordUser = (targetUser as SocketGuildUser);
                    if(execDiscordUser is null) {
                        if(users.Length > 1) {
                            exceptionList.Add(targetUser.Id);
                            continue;
                        } else {
                            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"An exception was caught. {targetUser.Mention} may not have been {(canBan ? "banned" : "kicked")} from the server.{(canBan ? $" \n\n**The DB Ban was applied to the user's account.**" : "")}"); });
                            return;
                        }
                    }
                    await (canBan ? execDiscordUser.BanAsync(0, reason) : execDiscordUser.KickAsync(reason));
                    if(users.Length > 1) {
                        kicklist.Add(targetUser.Id);
                    } else {
                        await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = $"{(canBan ? "Banned" : (banaccount ? "DB Banned & Kicked" : "Kicked"))} <@{targetUser.Id}> {(kickedWithoutDm ? "**without**" : "with")} DM"; });
                        return;
                    }
                    continue;
                } catch(Exception) {
                    if(users.Length > 1) {
                        exceptionList.Add(targetUser.Id);
                    } else {
                        await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"An exception was caught. {targetUser.Mention} may not have been {(canBan ? "banned" : "kicked")} from the server.{(canBan ? $" \n\n**The DB Ban was applied to the user's account.**" : "")}"); });
                        return;
                    }
                    continue;
                }
            }

            if(users.Length > 1) {
                var message = $"{(kicklist.Count > 0 ? "Kicked: " + string.Join(", ", kicklist.Select(id => $"<@{id}>")) : "")}";
                if(exceptionList.Count > 0) message += "\n\n**Did not kick**: " + string.Join(", ", exceptionList.Select(id => $"<@{id}>"));
                await Context.Interaction.ModifyOriginalResponseAsync(message);
            }
        }
    }
}
