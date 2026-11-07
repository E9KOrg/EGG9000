using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class ManageOverflow(IServiceProvider provider) : _UpdaterBase<ManageOverflow>(TimeSpan.FromMinutes(5.6), TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guilds = await db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);

            await HandleMemberTracking(db, guilds, cancellationToken);
            await HandleOverflowServers(guilds, cancellationToken);
        }

        public class BasicUserInfo {
            public ulong DiscordId { get; set; }
            public ulong GuildId { get; set; }
            public Guid Id { get; set; }
            public ulong? LastGuild { get; set; }
        }

        private async Task HandleMemberTracking(ApplicationDbContext db, List<Guild> guilds, CancellationToken cancellationToken) {
            var users = await db.DBUsers.Select(x => new BasicUserInfo { DiscordId = x.DiscordId, GuildId = x.GuildId, Id = x.Id, LastGuild = x.LastGuild }).ToListAsync(CancellationToken.None);

            foreach(var guild in guilds) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                var mainServer = _client.Guilds.FirstOrDefault(x => x.Id == guild.DiscordSeverId);
                if(mainServer is null)
                    continue;

                await mainServer.DownloadUsersAsync();

                await HandleMemberDepartures(db, guild, mainServer, users);
                await HandleMemberReturns(db, guild, mainServer, users);

                await db.SaveChangesAsync(CancellationToken.None);
                StillAlive();
            }
        }

        private async Task HandleMemberDepartures(ApplicationDbContext db, Guild guild, SocketGuild mainServer, List<BasicUserInfo> users) {
            if(!mainServer.HasAllMembers || mainServer.Users.Count == 0) {
                _logger.LogWarning("Skipping departure handling for {Name}: HasAllMembers={HasAll}, likely an incomplete member download", guild.Name, mainServer.HasAllMembers);
                return;
            }

            var missingFromCache = users.Where(x => x.GuildId == guild.Id && mainServer.GetUser(x.DiscordId) is null).ToList();

            var confirmedMissing = new List<Guid>();
            foreach(var candidate in missingFromCache) {
                if(await _client.Rest.GetGuildUserAsync(guild.DiscordSeverId, candidate.DiscordId) is null)
                    confirmedMissing.Add(candidate.Id);
                StillAlive();
            }

            if(confirmedMissing.Count == 0)
                return;

            var membersMissing = await db.DBUsers.Where(x => confirmedMissing.Contains(x.Id)).ToListAsync(CancellationToken.None);
            foreach(var member in membersMissing) {
                member.GuildId = 0;
                member.LastGuild = guild.Id;
                _logger.LogInformation("Removing member from guild {GuildName}: {MemberName}", guild.Name, member.DiscordUsername);
                StillAlive();
            }

            await PurgePendingAssignments(db, confirmedMissing, guild.Id);
        }

        private async Task HandleMemberReturns(ApplicationDbContext db, Guild guild, SocketGuild mainServer, List<BasicUserInfo> users) {
            var returnCandidates = users.Where(x => x.GuildId == 0 && mainServer.GetUser(x.DiscordId) is not null).ToList();
            if(returnCandidates.Count == 0)
                return;

            var confirmedReturned = new List<Guid>();
            foreach(var candidate in returnCandidates) {
                if(await _client.Rest.GetGuildUserAsync(guild.DiscordSeverId, candidate.DiscordId) is not null)
                    confirmedReturned.Add(candidate.Id);
                StillAlive();
            }

            if(confirmedReturned.Count == 0)
                return;

            var membersReturn = await db.DBUsers.Where(x => confirmedReturned.Contains(x.Id)).ToListAsync(CancellationToken.None);
            foreach(var member in membersReturn) {
                member.GuildId = guild.Id;
                _logger.LogInformation("Re-associating member to guild {GuildName}: {MemberName} (REST-confirmed present)", guild.Name, member.DiscordUsername);
                StillAlive();
            }
        }

        private async Task HandleOverflowServers(List<Guild> guilds, CancellationToken cancellationToken) {
            foreach(var guild in guilds.Where(x => x.OverflowServers.Count > 0)) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                var mainServer = _client.Guilds.FirstOrDefault(x => x.Id == guild.DiscordSeverId);
                if(mainServer is null)
                    continue;

                var overflowServers = _client.Guilds.Where(x => guild.OverflowServers.Contains(x.Id)).ToList();
                if(overflowServers.Count == 0)
                    continue;

                _logger.LogInformation("Managing overflow servers for {GuildName}", guild.Name);

                await mainServer.DownloadUsersAsync();
                foreach(var server in overflowServers)
                    await server.DownloadUsersAsync();

                await SyncOverflowSettings(guild, mainServer, overflowServers, cancellationToken);
                await ManageOverflowRoles(mainServer, overflowServers, cancellationToken);
                await ManageOverflowMembers(mainServer, overflowServers, cancellationToken);

                StillAlive();
            }
        }

        private async Task SyncOverflowSettings(Guild guild, SocketGuild mainServer, List<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            try {
                await OverflowSyncing.HandleChannelPermissionSyncsAsync(_client, mainServer, overflowServers, _logger, cancellationToken);
            } catch(Exception ex) {
                _logger.LogError(ex, "Error syncing coop category permissions for {GuildName}", guild.Name);
            }
            StillAlive();

            try {
                await OverflowSyncing.HandleRoleSyncsAsync(guild, mainServer, overflowServers, _logger, cancellationToken);
            } catch(Exception ex) {
                _logger.LogError(ex, "Error syncing roles for {GuildName}", guild.Name);
            }
            StillAlive();
        }

        private async Task ManageOverflowRoles(SocketGuild mainServer, List<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            var role = mainServer.GetRole(KnownRoles.Overflow);
            if(role is null) {
                _logger.LogWarning("Unable to find overflow role in {ServerName}", mainServer.Name);
                return;
            }

            bool InAllOverflows(SocketGuildUser user) => overflowServers.All(o => o.GetUser(user.Id) is not null);
            bool IsRegistered(SocketGuildUser user) => user.Roles.Any(r => r.Id == KnownRoles.Registered);
            bool HasOverflowRole(SocketGuildUser user) => user.Roles.Any(r => r.Id == KnownRoles.Overflow);

            var humans = mainServer.Users.Where(x => !x.IsBot).ToList();
            var needsRole = humans.Where(x => !InAllOverflows(x) && !HasOverflowRole(x) && IsRegistered(x) && x.Roles.Count > 2).ToList();
            var doneWithRole = humans.Where(x => HasOverflowRole(x) && (InAllOverflows(x) || !IsRegistered(x))).ToList();

            foreach(var user in needsRole) {
                if(cancellationToken.IsCancellationRequested)
                    break;
                await WaitOnCoopsBeingCreated(cancellationToken);
                await user.AddRoleAsync(role);
                _logger.LogInformation("Added overflow role to {UserName}", user.GetName());
                StillAlive();
            }

            foreach(var user in doneWithRole) {
                if(cancellationToken.IsCancellationRequested)
                    break;
                await WaitOnCoopsBeingCreated(cancellationToken);
                await user.RemoveRoleAsync(role);
                _logger.LogInformation("Removed overflow role from {UserName}, they are {Reason}", user.GetName(), IsRegistered(user) ? "in all overflow servers" : "not registered");
                StillAlive();
            }
        }

        private async Task ManageOverflowMembers(SocketGuild mainServer, List<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            foreach(var overflowServer in overflowServers) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                await WaitOnCoopsBeingCreated(cancellationToken);

                if(!mainServer.HasAllMembers) {
                    _logger.LogWarning("Skipping overflow kicks for {ServerName}: main server member download incomplete", overflowServer.Name);
                } else {
                    foreach(var user in overflowServer.Users.Where(x => !x.IsBot && mainServer.GetUser(x.Id) is null).ToList()) {
                        await user.KickAsync("No longer in main server");
                        _logger.LogInformation("Kicked {UserName} from {ServerName}, not in main server", user.GetName(), overflowServer.Name);
                        StillAlive();
                    }
                }

                await SyncMemberNicknames(mainServer, overflowServer, cancellationToken);
                StillAlive();
            }
        }

        private async Task SyncMemberNicknames(SocketGuild mainServer, SocketGuild overflowServer, CancellationToken cancellationToken) {
            foreach(var overflowUser in overflowServer.Users.ToList()) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                var mainServerUser = mainServer.GetUser(overflowUser.Id);
                if(mainServerUser is null || overflowUser.IsBot || overflowServer.OwnerId == overflowUser.Id || overflowUser.Nickname == mainServerUser.Nickname)
                    continue;

                await WaitOnCoopsBeingCreated(cancellationToken);
                try {
                    await overflowUser.ModifyAsync(x => x.Nickname = mainServerUser.Nickname);
                    _logger.LogInformation("Updated nickname for {UserName} in {ServerName}", mainServerUser.GetName(), overflowServer.Name);
                } catch(Exception ex) {
                    _logger.LogWarning(ex, "Unable to change nickname for {UserName} in {ServerName}", mainServerUser.GetName(), overflowServer.Name);
                }
                StillAlive();
            }
        }

        private async Task PurgePendingAssignments(ApplicationDbContext db, List<Guid> departedUserIds, ulong guildId) {
            if(departedUserIds.Count == 0)
                return;

            var staleXrefs = await db.UserCoopXrefs
                .Where(PendingAssignmentPurgeFilter(departedUserIds, guildId, DateTimeOffset.UtcNow))
                .Select(x => new { x.UserId, x.Coop.ContractID, Xref = x })
                .ToListAsync(CancellationToken.None);

            if(staleXrefs.Count == 0)
                return;

            db.UserCoopXrefs.RemoveRange(staleXrefs.Select(x => x.Xref));
            var lookup = _provider.GetService<CoopAssignmentLookup>();
            foreach(var stale in staleXrefs) {
                lookup?.Remove(stale.UserId, stale.ContractID);
                _logger.LogInformation("Purged pending coop assignment for departed user {UserId} in contract {Contract}", stale.UserId, stale.ContractID);
                StillAlive();
            }
        }

        public static Expression<Func<UserCoopXref, bool>> PendingAssignmentPurgeFilter(List<Guid> departedUserIds, ulong guildId, DateTimeOffset now) =>
            x => departedUserIds.Contains(x.UserId)
              && !x.JoinedCoop
              && x.Coop.GuildId == guildId
              && (int)x.Coop.Status > 2 && (int)x.Coop.Status < 13
              && x.Coop.CoopEnds > now && !x.Coop.PseudoExpired;
    }
}
