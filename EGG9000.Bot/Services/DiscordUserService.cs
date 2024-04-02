
using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Commands;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {

    public class DiscordUserService(DiscordHostedService discord, Bugsnag.IClient bugsnag, IServiceProvider provider, ILogger<DiscordUserService> logger) : IHostedService {

        private readonly DiscordHostedService _discord = discord;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;
        private readonly IServiceProvider _provider = provider;
        private readonly ILogger<DiscordUserService> _logger = logger;

#if DEV9002 || DEBUG
        private static readonly bool _debug = false;
#else
        private static readonly bool _debug = true;
#endif

        public Task StartAsync(CancellationToken cancellationToken) {
            _discord.UserJoined += Client_UserJoined;
            _discord.UserLeft += Client_UserLeft;
            _discord.ChannelDestroyed += _discord_ChannelDestroyed;
            return Task.CompletedTask;
        }

        private Task _discord_ChannelDestroyed(SocketChannel arg) {
            _ = HandleChannelDeleted(arg);
            return Task.CompletedTask;
        }

        private async Task HandleChannelDeleted(SocketChannel arg) {
            var db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guildContract = await db.GuildContracts.FirstOrDefaultAsync(x => x.DiscordChannelId == arg.Id);
            if(guildContract is not null) {
                guildContract.DeletedChannel = true;
                await db.SaveChangesAsync();
            }
            var coop = await db.Coops.FirstOrDefaultAsync(x => x.ThreadID == arg.Id || x.DiscordChannelId == arg.Id);
            if(coop is not null && coop.ThreadID != 0) {
                coop.ThreadArchived = true;
                await db.SaveChangesAsync();
            } else if(coop is not null && coop.DiscordChannelId != 0) {
                coop.DeletedChannel = true;
                await db.SaveChangesAsync();
            }

        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _discord.UserJoined -= Client_UserJoined;
            _discord.UserLeft -= Client_UserLeft;
            _discord.ChannelDestroyed -= _discord_ChannelDestroyed;
            return Task.CompletedTask;
        }

        private Task Client_UserJoined(SocketGuildUser user) {
            _ = Client_UserJoined_Task(user);
            return Task.CompletedTask;
        }

        private async Task Client_UserJoined_Task(SocketGuildUser user) {
            var db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await UserJoined(user, db);
        }

        private Task Client_UserLeft(SocketGuild guild, SocketUser user) {
            _ = Client_UserLeft_Task(guild, user);
            return Task.CompletedTask;
        }

        private async Task Client_UserLeft_Task(SocketGuild guild, SocketUser user) {
            var db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await UserLeft(guild, user, db);
        }

        public async Task UserJoined(SocketGuildUser user, ApplicationDbContext db) {
            if(user.IsBot)
                return;


            var guilds = await db.Guilds.ToListAsync();
            var dbguild = guilds.FirstOrDefault(x => x.DiscordSeverId == user.Guild.Id);
            if(dbguild == null) {
                dbguild = guilds.FirstOrDefault(x => x.OverflowServers.Any(y => y == user.Guild.Id));
                if(dbguild != null) {
                    //Handle Overflow Role
                    var mainServer = _discord.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                    var overflowServers = _discord.Guilds.Where(x => dbguild.OverflowServers.Contains(x.Id));
                    const ulong overflowRoleID = 775547850134257675;

                    bool inMainWithRole = mainServer.Users.Any(u => u.Id == user.Id && u.Roles.Any(r => r.Id == overflowRoleID)),
                        inAllOverFlows = overflowServers.All(o => o.Users.Any(u => u.Id == user.Id) || o.Id == user.Guild.Id);
                    if(inMainWithRole && inAllOverFlows) {
                        _logger.LogInformation("Removing overflow role for {user}, they joined all overflows", mainServer.Users.First(u => u.Id == user.Id).GetName());
                        await mainServer.Users.First(u => u.Id == user.Id).RemoveRoleAsync(overflowRoleID);
                    }

                    //Handle assigned co-ops
                    try {
                        var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.User.DiscordId == user.Id && x.Coop.OverflowGuildId == user.Guild.Id && !x.Coop.ThreadArchived && !x.AddedToChannel).ToListAsync();
                        foreach(var xref in xrefs) {
                            var coopChannel = xref.Coop.ThreadID != 0 ? (SocketThreadChannel)_discord.GetChannel(xref.Coop.ThreadID) : (SocketTextChannel)_discord.GetChannel(xref.Coop.DiscordChannelId);
                            await coopChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                            xref.AddedToChannel = true;
                            await coopChannel.SendMessageAsync($"Here is your co-op {user.Mention}! The co-op name to join is {xref.Coop.Name}");
                        }
                        if(xrefs.Any()) {
                            await db.SaveChangesAsync();
                        }
                    } catch(Exception e) {
                        _bugsnag.Notify(e);
                    }
                }
                return;
            }

            
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser is not null && dbuser.TempDisabled) {
                var welChannel = await _discord.GetChannelAsync(GuildChannelType.Welcome, user.Guild);
                var disabledMsg = $"Welcome to the server {user.Mention}! Looks like you are currently disabled, please wait for someone from staff to get you re-enabled.";
                await welChannel.SendMessageAsync(disabledMsg);
                await ChannelHelper.DetermineAndSend(db, _discord, dbguild, _discord.GetGuild(user.Guild.Id), GuildChannelType.BannedUserThread, new() { Text = $"{user.Mention} just joined and is disabled." }, _logger);
            }

            if(dbuser != null && dbuser.GuildId == user.Guild.Id) {
                await DiscordHelpers.CheckRoles(db, user.Guild, user, dbuser, _discord, null, []);
                var response = await ChannelHelper.DetermineAndSend(db, _discord, dbguild, _discord.GetGuild(dbuser.GuildId), GuildChannelType.General, new() { Text = $"Welcome back {user.Mention}!" }, _logger);
                await RegisterCommandsSlash.CleanWelcomeChannel(user.Guild, _discord, user);
                return;
            } else if(dbuser is not null && dbuser.GuildId == 0) {
                var previouslyHere = await db.UserCoopXrefs.AnyAsync(x => x.UserId == dbuser.Id && x.Coop.GuildId == user.Guild.Id);
                if(previouslyHere) {
                    dbuser.GuildId = user.Guild.Id;
                    dbuser.UpdateAccounts();
                    await db.SaveChangesAsync();
                    await DiscordHelpers.CheckRoles(db, user.Guild, user, dbuser, _discord, null, []);
                    var response = await ChannelHelper.DetermineAndSend(db, _discord, dbguild, _discord.GetGuild(dbuser.GuildId), GuildChannelType.General, new() { Text = $"Welcome back {user.Mention}!" }, _logger);
                    await RegisterCommandsSlash.CleanWelcomeChannel(user.Guild, _discord, user);
                    return;
                }
            }

            if(_debug) return;

            var welcomeChannel = await _discord.GetChannelAsync(GuildChannelType.Welcome, user.Guild);
            var rulesChannel = await _discord.GetChannelAsync(GuildChannelType.Rules, user.Guild);
            var msg = $"Welcome to the server {user.Mention}! Please read {rulesChannel.Mention} and then use the </accept:1095116354329268368> command when you are ready.";
            var talkChannel = ChannelHelper.DetermineChannelType(dbguild, _discord.GetGuild(dbguild.DiscordSeverId), GuildChannelType.TalkToStaff);
            var talkChannelMention = talkChannel != null ? (talkChannel.GetType() == typeof(SocketTextChannel) ? ((SocketTextChannel)talkChannel).Mention
                : ((SocketThreadChannel)talkChannel).Mention) : null;
            if(talkChannelMention != null) msg += $" If you have any questions feel free to ask us in {talkChannelMention}, we are glad you are here!";

            await welcomeChannel.SendMessageAsync(msg);
        }

        public async Task UserLeft(SocketGuild guild, SocketUser user, ApplicationDbContext db) {
            if(user.IsBot)
                return;
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

            await RegisterCommandsSlash.CleanWelcomeChannel(guild, _discord, user);

            if(guild.Id != dbuser?.GuildId)
                return;

            if(dbuser != null) {
                dbuser.AcceptedRules = false;
                dbuser.GuildId = 0;
                await db.SaveChangesAsync();
            }
        }

    }
}
