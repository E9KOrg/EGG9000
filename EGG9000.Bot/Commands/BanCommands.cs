using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Helpers.Discord.Paging;
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
    public class BanListPager(IReadOnlyList<string> lines, int page, string guildName, ulong invokerId) : TextListPager(lines, page) {
        protected override string Title => "Banned Users";
        protected override string Preamble => $"Users Banned from {guildName}";
        protected override Color EmbedColor => Color.DarkRed;
        protected override string CustomIdPrefix => "BanListPage";
        protected override string KeySuffix => $"{invokerId}";

        public static (ulong InvokerId, int Page) ParseCustomId(string data) {
            var parts = data.Split(",");
            return (ulong.Parse(parts[0]), int.Parse(parts[1]));
        }
    }

    [Group("b", "Ban management commands")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [StaffOnly(StaffTier.Admin)]
    public partial class BanGroupModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client) : E9KModuleBase(dbFactory) {
        private readonly DiscordHostedService _client = client;

        private async Task<List<DBUser>> FetchBannedUsers(ulong guildId) {
            var resolvedGuildId = (await Db.Guilds.FirstOrDefaultAsync(g => g.Id == guildId || g.OverflowServersJson.Contains(guildId.ToString())))?.Id ?? ulong.MaxValue;
            return await Db.DBUsers.Where(u => (u.Banned && (u.LastGuild == resolvedGuildId || u.GuildId == resolvedGuildId)) || (u.ServersBannedFrom != null && u.ServersBannedFrom.IndexOf(resolvedGuildId.ToString()) > -1)).ToListAsync();
        }

        private static List<string> BuildBanLines(List<DBUser> bannedUsers) =>
            [.. bannedUsers.Select(u => $"{u.DiscordUsername}\t{u.DiscordId}\t" + string.Join(", ", u.EggIncAccounts.Select(a => a.Id)))];

        [SlashCommand("banlist", "Check the list of Users/EIDs that have been banned from the server via /kick")]
        public async Task BanList() {
            await Context.Interaction.DeferAsync();
            var bannedUsers = await FetchBannedUsers(Context.Interaction.GuildId ?? ulong.MaxValue);
            if(bannedUsers is null || bannedUsers.Count == 0) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("No users are banned from this guild."); });
                return;
            }
            var guildName = (await Db.Guilds.FirstOrDefaultAsync(x => x.Id == Context.Interaction.GuildId)).Name;

            var pager = new BanListPager(BuildBanLines(bannedUsers), 0, guildName, Context.User.Id);
            await pager.SendAsync(Context.Interaction);
        }

        [ComponentInteraction("BanListPage:*", ignoreGroupNames: true)]
        public async Task BanListPage(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var (invokerId, page) = BanListPager.ParseCustomId(data);
            if(component.User.Id != invokerId) { await Pager.RejectNonInvokerAsync(component); return; }

            var bannedUsers = await FetchBannedUsers(component.GuildId ?? ulong.MaxValue);
            var guildName = (await Db.Guilds.FirstOrDefaultAsync(x => x.Id == component.GuildId)).Name;
            var pager = new BanListPager(BuildBanLines(bannedUsers), page, guildName, invokerId);
            await pager.UpdateComponentAsync(component);
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

        [SlashCommand("ban", "Kick user(s) with DM and ban their EID(s) from the server")]
        public async Task Ban(
            [Summary("reason", "reason")] string reason,
            [ComplexParameter] UserSlots userSlots) {
            await Context.Interaction.DeferAsync();
            var users = userSlots.Users;
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == Context.Channel.Id));
            var dbGuild = await Db.Guilds.FirstOrDefaultAsync(g => g.Id == Context.Interaction.GuildId || g.OverflowServersJson.Contains(Context.Interaction.GuildId.ToString()));
            var runningUser = _client.Guilds?.FirstOrDefault(g => g.Id == Context.Interaction.GuildId)?.Users?.ToList().FirstOrDefault(u => u.Id == Context.User.Id);
            var canBan = runningUser is not null && runningUser.GuildPermissions.ToList().Contains(GuildPermission.BanMembers);

            var banlist = new List<ulong>();
            var exceptionList = new List<ulong>();
            foreach(var targetUser in users) {
                var bannedWithoutDm = false;
                var dmChannel = await targetUser.CreateDMChannelAsync();

                if(await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id) is { } dbUser) {
                    var bannedServersList = dbUser.ServersBannedFrom?.Split(",")?.ToList() ?? [];
                    bannedServersList.Add(dbGuild.Id.ToString());
                    dbUser.ServersBannedFrom = string.Join(",", bannedServersList);
                    dbUser.Banned = true;
                    await Db.SaveChangesAsync();
                }

                try {
                    await dmChannel.SendMessageAsync($"You have been banned from {guild.Name} for the reason: {reason}.");
                } catch(HttpException) {
                    bannedWithoutDm = true;
                }

                try {
                    var execDiscordUser = guild.GetUser(targetUser.Id);
                    if(execDiscordUser is null || !canBan) {
                        if(users.Length > 1) {
                            exceptionList.Add(targetUser.Id);
                            continue;
                        } else {
                            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"{targetUser.Mention} may not have been banned from the server via Discord.{(canBan ? "" : " You do not have the `BanMembers` permission.")}\n\n**The DB Ban was applied to the user's account.**"); });
                            return;
                        }
                    }
                    await execDiscordUser.BanAsync(0, reason);
                    if(users.Length > 1) {
                        banlist.Add(targetUser.Id);
                    } else {
                        await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = $"Banned <@{targetUser.Id}> {(bannedWithoutDm ? "**without**" : "with")} DM"; });
                        return;
                    }
                    continue;
                } catch(Exception) {
                    if(users.Length > 1) {
                        exceptionList.Add(targetUser.Id);
                    } else {
                        await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"An exception was caught. {targetUser.Mention} may not have been banned from the server.\n\n**The DB Ban was applied to the user's account.**"); });
                        return;
                    }
                    continue;
                }
            }

            if(users.Length > 1) {
                var message = $"{(banlist.Count > 0 ? "Banned: " + string.Join(", ", banlist.Select(id => $"<@{id}>")) : "")}";
                if(exceptionList.Count > 0) message += "\n\n**Did not Discord-ban (DB ban still applied)**: " + string.Join(", ", exceptionList.Select(id => $"<@{id}>"));
                await Context.Interaction.ModifyOriginalResponseAsync(message);
            }
        }
    }

    // Flat (non-grouped) command. Was a top-level /kick before the Discord.NET migration and was
    // incorrectly nested under /admin in that migration - kept flat here to preserve the
    // pre-migration command name.
    public class KickModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client) : E9KModuleBase(dbFactory) {
        private readonly DiscordHostedService _client = client;

        [SlashCommand("kick", "Kick user(s) with DM")]
        [DefaultMemberPermissions(GuildPermission.ManageChannels)]
        [StaffOnly(StaffTier.CluckingCoordinator)]
        public async Task Kick(
            [Summary("reason", "reason")] string reason,
            [ComplexParameter] UserSlots userSlots) {
            await Context.Interaction.DeferAsync();
            var users = userSlots.Users;
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == Context.Channel.Id));

            var kicklist = new List<ulong>();
            var exceptionList = new List<ulong>();
            foreach(var targetUser in users) {
                var kickedWithoutDm = false;
                var dmChannel = await targetUser.CreateDMChannelAsync();

                try {
                    await dmChannel.SendMessageAsync($"You have been kicked from {guild.Name} for the reason: {reason}.");
                } catch(HttpException) {
                    kickedWithoutDm = true;
                }

                try {
                    var execDiscordUser = guild.GetUser(targetUser.Id);
                    if(execDiscordUser is null) {
                        if(users.Length > 1) {
                            exceptionList.Add(targetUser.Id);
                            continue;
                        } else {
                            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"An exception was caught. {targetUser.Mention} may not have been kicked from the server."); });
                            return;
                        }
                    }
                    await execDiscordUser.KickAsync(reason);
                    if(users.Length > 1) {
                        kicklist.Add(targetUser.Id);
                    } else {
                        await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = $"Kicked <@{targetUser.Id}> {(kickedWithoutDm ? "**without**" : "with")} DM"; });
                        return;
                    }
                    continue;
                } catch(Exception) {
                    if(users.Length > 1) {
                        exceptionList.Add(targetUser.Id);
                    } else {
                        await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"An exception was caught. {targetUser.Mention} may not have been kicked from the server."); });
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
