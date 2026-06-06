using Discord;
using Discord.Net;
using Discord.WebSocket;
using EGG9000.Common.Database;
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
    public static class BanCommands {

        [SlashCommand(Description = "Check the list of Users/EIDs that have been banned from the server via /kick", ParentCommand = "b", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task BanList(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync();
            var guildId = (await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString())))?.Id ?? ulong.MaxValue;
            var bannedUsers = await db.DBUsers.Where(u => (u.Banned && (u.LastGuild == guildId || u.GuildId == guildId)) || (u.ServersBannedFrom != null && u.ServersBannedFrom.IndexOf(guildId.ToString()) > -1)).ToListAsync();
            if(bannedUsers is null || bannedUsers.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("No users are banned from this guild."); });
                return;
            }
            var userList = string.Join("\n", bannedUsers.Select(u => $"{u.DiscordUsername}\t{u.DiscordId}\t" + string.Join(", ", u.EggIncAccounts.Select(a => a.Id).ToList()))) ?? "Could not compile list";
            var guildName = (await db.Guilds.FirstOrDefaultAsync(x => x.Id == command.GuildId)).Name;

            var responseEmbedBuilder = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder().WithName("Banned Users").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).WithColor(Color.DarkRed)
                        .WithDescription($"Users Banned from {guildName}\n\n" +
                        $"{(userList.Length > 1600 ? "_(List too large for Discord - see attached file)_\n" : userList)}");

            if(userList.Length > 1600) await command.RespondWithFileAsync(new FileAttachment(new MemoryStream(Encoding.UTF8.GetBytes(userList.Replace("<@", "").Replace(">", ""))), "BannedUsers.txt"), text: "", embed: responseEmbedBuilder.Build());
            else await command.RespondAsync(content: "", embed: responseEmbedBuilder.Build());
        }

        [SlashCommand(Description = "Remove the ban placed on a user, and their associated EID(s)", ParentCommand = "b", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task RemoveBan(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam(Description = "Discord ID of user to unban")] SocketUser user) {
            await command.DeferAsync();
            var dbBanMessage = "";
            var dbuser = db.DBUsers.FirstOrDefault(u => u.DiscordId == user.Id);
            if(dbuser is not null && dbuser.Banned) {
                var dbGuild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString()));
                var bannedServersList = dbuser.ServersBannedFrom?.Split(",").ToList() ?? [];
                var wasDbBanned = bannedServersList.Contains(dbGuild.Id.ToString());
                if(wasDbBanned) {
                    bannedServersList.Remove(dbGuild.Id.ToString());
                    dbuser.ServersBannedFrom = string.Join(",", bannedServersList);
                }
                dbuser.Banned = false;
                await db.SaveChangesAsync();
                dbBanMessage = "User's DB ban was removed";
            } else {
                dbBanMessage = "No banned DBUser entry found for this user.";
            }

            var discordBanMessage = "";
            var socketGuild = _client.GetGuild(command.GuildId ?? ulong.MaxValue);
            var targetUser = socketGuild.GetUser(user.Id) ?? await _client.Gateway.GetUserAsync(user.Id);
            var runningUser = socketGuild?.Users?.ToList().FirstOrDefault(u => u.Id == command.User.Id);
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

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = unbanEmbed; });
        }

        [SlashCommand(Description = "Kick user with dm", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task Kick(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketUser[] users, [SlashParam] string reason, [SlashParam(Required = false)] bool banaccount = false) {
            await command.DeferAsync();
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var dbGuild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString()));

            var kicklist = new List<ulong>();
            var exceptionList = new List<ulong>();
            foreach(var targetUser in users) {
                var kickedWithoutDm = false;
                var dmChannel = await targetUser.CreateDMChannelAsync();

                if(banaccount && await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id) is { } dbUser) {
                    var bannedServersList = dbUser.ServersBannedFrom?.Split(",")?.ToList() ?? [];
                    bannedServersList.Add(dbGuild.Id.ToString());
                    dbUser.ServersBannedFrom = string.Join(",", bannedServersList);
                    dbUser.Banned = true;
                    await db.SaveChangesAsync();
                }

                try {
                    var verbiage = banaccount ? "banned" : "kicked";
                    await dmChannel.SendMessageAsync($"You have been {verbiage} from {guild.Name} for the reason: {reason}.");
                } catch(HttpException) {
                    kickedWithoutDm = true;
                }

                var runningUser = _client.Guilds?.FirstOrDefault(g => g.Id == command.GuildId)?.Users?.ToList().FirstOrDefault(u => u.Id == command.User.Id);
                var canBan = banaccount && runningUser is not null && runningUser.GuildPermissions.ToList().Contains(GuildPermission.BanMembers);

                try {
                    var execDiscordUser = (targetUser as SocketGuildUser);
                    if(execDiscordUser is null) {
                        if(users.Length > 1) {
                            exceptionList.Add(targetUser.Id);
                            continue;
                        } else {
                            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"An exception was caught. {targetUser.Mention} may not have been {(canBan ? "banned" : "kicked")} from the server.{(canBan ? $" \n\n**The DB Ban was applied to the user's account.**" : "")}"); });
                            return;
                        }
                    }
                    await (canBan ? execDiscordUser.BanAsync(0, reason) : execDiscordUser.KickAsync(reason));
                    if(users.Length > 1) {
                        kicklist.Add(targetUser.Id);
                    } else {
                        await command.ModifyOriginalResponseAsync(x => { x.Content = $"{(canBan ? "Banned" : (banaccount ? "DB Banned & Kicked" : "Kicked"))} <@{targetUser.Id}> {(kickedWithoutDm ? "**without**" : "with")} DM"; });
                        return;
                    }
                    continue;
                } catch(Exception) {
                    if(users.Length > 1) {
                        exceptionList.Add(targetUser.Id);
                    } else {
                        await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"An exception was caught. {targetUser.Mention} may not have been {(canBan ? "banned" : "kicked")} from the server.{(canBan ? $" \n\n**The DB Ban was applied to the user's account.**" : "")}"); });
                        return;
                    }
                    continue;
                }
            }

            if(users.Length > 1) {
                var message = $"{(kicklist.Count > 0 ? "Kicked: " + string.Join(", ", kicklist.Select(id => $"<@{id}>")) : "")}";
                if(exceptionList.Count > 0) message += "\n\n**Did not kick**: " + string.Join(", ", exceptionList.Select(id => $"<@{id}>"));
                await command.ModifyOriginalResponseAsync(message);
            }
        }
    }
}
