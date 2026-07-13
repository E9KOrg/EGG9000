using Discord;
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
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);

            // Handle member departures and returns across all guilds
            await HandleMemberTracking(_db, guilds, cancellationToken);

            // Handle overflow server management for guilds with overflow servers
            await HandleOverflowServers(guilds, cancellationToken);
        }

        public class BasicUserInfo {
            public ulong DiscordId { get; set; }
            public ulong GuildId { get; set; }
            public Guid Id { get; set; }
            public ulong? LastGuild { get; set; }
        }

        /// <summary>
        /// Track member departures and returns across all guilds using REST lookups for accuracy.
        /// </summary>
        private async Task HandleMemberTracking(ApplicationDbContext db, List<Guild> guilds, CancellationToken cancellationToken) {
            var users = await db.DBUsers.Select(x => new BasicUserInfo { DiscordId = x.DiscordId, GuildId = x.GuildId, Id = x.Id, LastGuild = x.LastGuild }).ToListAsync(CancellationToken.None);
            
            foreach (var guild in guilds) {
                if (cancellationToken.IsCancellationRequested) break;

                var mainServer = _client.Guilds.FirstOrDefault(x => x.Id == guild.DiscordSeverId);
                if (mainServer is null)
                    continue;

                await mainServer.DownloadUsersAsync();

                // Handle member departures
                await HandleMemberDepartures(db, guild, mainServer, users, cancellationToken);

                // Handle member returns
                await HandleMemberReturns(db, guild, mainServer, users, cancellationToken);

                await db.SaveChangesAsync(CancellationToken.None);
                StillAlive();
            }
        }

        /// <summary>
        /// Handle members who have left the guild.
        /// </summary>
        private async Task HandleMemberDepartures(ApplicationDbContext db, Guild guild, SocketGuild mainServer, List<BasicUserInfo> users, CancellationToken cancellationToken) {
            var members = users.Where(x => x.GuildId == guild.Id).ToList();
            var missingFromCache = members.Where(x => mainServer.GetUser(x.DiscordId) is null).ToList();

            if (!mainServer.HasAllMembers || mainServer.Users.Count == 0) {
                _logger.LogWarning("Skipping departure handling for {name}: HasAllMembers={hasAll}, likely an incomplete member download", guild.Name, mainServer.HasAllMembers);
                return;
            }

            // REST-confirm each candidate to avoid false positives from stale cache
            var confirmedMissing = new List<Guid>();
            foreach (var candidate in missingFromCache) {
                var restUser = await _client.Rest.GetGuildUserAsync(guild.DiscordSeverId, candidate.DiscordId);
                if (restUser is null)
                    confirmedMissing.Add(candidate.Id);
                StillAlive();
            }

            if (confirmedMissing.Count == 0)
                return;

            // Mark members as departed
            var membersMissing = await db.DBUsers.Where(x => confirmedMissing.Contains(x.Id)).ToListAsync(CancellationToken.None);
            membersMissing.ForEach(x => {
                x.GuildId = 0;
                x.LastGuild = guild.Id;
                _logger.LogInformation("Removing member from guild {guildName}: {memberName}", guild.Name, x.DiscordUsername);
                StillAlive();
            });

            // Purge pending coop assignments for departed members
            await PurgePendingAssignments(db, confirmedMissing, guild.Id);
        }

        /// <summary>
        /// Handle members who have returned to the guild.
        /// </summary>
        private async Task HandleMemberReturns(ApplicationDbContext db, Guild guild, SocketGuild mainServer, List<BasicUserInfo> users, CancellationToken cancellationToken) {
            var returnCandidates = users.Where(x => x.GuildId == 0 && mainServer.GetUser(x.DiscordId) is not null).ToList();

            if (returnCandidates.Count == 0)
                return;

            // REST-confirm each candidate to avoid false positives from stale cache
            var confirmedReturned = new List<Guid>();
            foreach (var candidate in returnCandidates) {
                var restUser = await _client.Rest.GetGuildUserAsync(guild.DiscordSeverId, candidate.DiscordId);
                if (restUser is not null)
                    confirmedReturned.Add(candidate.Id);
                StillAlive();
            }

            if (confirmedReturned.Count == 0)
                return;

            // Re-associate returned members
            var membersReturn = await db.DBUsers.Where(x => confirmedReturned.Contains(x.Id)).ToListAsync(CancellationToken.None);
            membersReturn.ForEach(x => {
                x.GuildId = guild.Id;
                _logger.LogInformation("Re-associating member to guild {guildName}: {memberName} (REST-confirmed present)", guild.Name, x.DiscordUsername);
                StillAlive();
            });
        }

        /// <summary>
        /// Handle overflow server management: syncing, role management, and member management.
        /// </summary>
        private async Task HandleOverflowServers(List<Guild> guilds, CancellationToken cancellationToken) {
            foreach (var guild in guilds.Where(x => x.OverflowServers.Count > 0)) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _logger.LogInformation("Managing overflow servers for {guildName}", guild.Name);

                var mainServer = _client.Guilds.FirstOrDefault(x => x.Id == guild.DiscordSeverId);
                if (mainServer is null)
                    continue;

                var overflowServers = _client.Guilds.Where(x => guild.OverflowServers.Contains(x.Id)).ToList();
                if (overflowServers.Count == 0)
                    continue;

                // Download all users
                await mainServer.DownloadUsersAsync();
                foreach (var server in overflowServers) {
                    await server.DownloadUsersAsync();
                }

                // Sync settings from main to overflow servers
                await SyncOverflowSettings(guild, mainServer, overflowServers, cancellationToken);

                // Manage overflow role assignments
                await ManageOverflowRoles(mainServer, overflowServers, cancellationToken);

                // Manage member access and nicknames
                await ManageOverflowMembers(mainServer, overflowServers, cancellationToken);

                StillAlive();
            }
        }

        /// <summary>
        /// Sync channel permissions, role configurations, and command permissions from main to overflow servers.
        /// </summary>
        private async Task SyncOverflowSettings(Guild guild, SocketGuild mainServer, List<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            try {
                // Sync channel permissions
                await HandleChannelPermissionSyncs(mainServer, overflowServers, cancellationToken);

                // Sync role configurations
                await OverflowSyncing.HandleRoleSyncsAsync(guild, mainServer, overflowServers, _provider, _logger, cancellationToken);

                // Sync application command permissions
                //await HandleApplicationCommandPermissionSyncs(guild, mainServer, overflowServers, cancellationToken);

                StillAlive();
            } catch (Exception ex) {
                _logger.LogError(ex, "Error syncing overflow settings for {guildName}", guild.Name);
            }
        }

        /// <summary>
        /// Manage overflow role assignments for users based on their server presence.
        /// </summary>
        private async Task ManageOverflowRoles(SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            const ulong overflowRoleID = 775547850134257675;
            const ulong registeredRoleID = 794713762396897280;

            var overflowServerList = overflowServers.ToList();
            var role = mainServer.Roles.FirstOrDefault(x => x.Id == overflowRoleID);

            if (role is null) {
                _logger.LogWarning("Unable to find overflow role in main server");
                return;
            }

            // Find users who should have the overflow role
            var onlyMain = mainServer.Users.Where(x => !overflowServerList.All(o => o.Users.Any(y => y.Id == x.Id)) && !x.IsBot);
            var allOverflows = mainServer.Users.Where(x => (overflowServerList.All(o => o.Users.Any(y => y.Id == x.Id)) || !x.Roles.Any(y => y.Id == registeredRoleID)) && !x.IsBot);

            var bothAllWithRole = allOverflows.Where(x => x.Roles.Any(y => y.Id == overflowRoleID));
            var onlyMainWithoutRole = onlyMain.Where(x => !x.Roles.Any(y => y.Id == overflowRoleID) && x.Roles.Count > 2 && x.Roles.Any(y => y.Id == registeredRoleID));

            // Add overflow role to users who are only in main
            foreach (var user in onlyMainWithoutRole) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await WaitOnCoopsBeingCreated(cancellationToken);
                await user.AddRoleAsync(role);
                _logger.LogInformation("Added overflow role to {userName}", user.GetName());
                StillAlive();
            }

            // Remove overflow role from users with only that role
            foreach (var user in mainServer.Users.Where(x => x.Roles.Count == 1 && x.Roles.Any(y => y.Id == overflowRoleID) && !x.IsBot)) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await WaitOnCoopsBeingCreated(cancellationToken);
                await user.RemoveRoleAsync(role);
                _logger.LogInformation("Removed overflow role from {userName} (was only role)", user.GetName());
                StillAlive();
            }

            // Remove overflow role from users in all servers
            foreach (var user in bothAllWithRole) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await WaitOnCoopsBeingCreated(cancellationToken);
                await user.RemoveRoleAsync(role);
                _logger.LogInformation("Removed overflow role from {userName} (in all servers)", user.GetName());
                StillAlive();
            }
        }

        /// <summary>
        /// Manage member access and synchronization across overflow servers.
        /// </summary>
        private async Task ManageOverflowMembers(SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            foreach (var overflowServer in overflowServers) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await WaitOnCoopsBeingCreated(cancellationToken);

                // Kick members who are no longer in main server
                var onlyOverflow = overflowServer.Users.Where(x => !mainServer.Users.Any(y => y.Id == x.Id) && !x.IsBot);
                foreach (var user in onlyOverflow) {
                    await user.KickAsync("No longer in main server");
                    _logger.LogInformation("Kicked {userName} from overflow server {serverName} (not in main)", user.GetName(), overflowServer.Name);
                    StillAlive();
                }

                // Sync nicknames for members present in both servers
                await SyncMemberNicknames(mainServer, overflowServer, cancellationToken);

                StillAlive();
            }
        }

        /// <summary>
        /// Sync member nicknames from main server to overflow servers.
        /// </summary>
        private async Task SyncMemberNicknames(SocketGuild mainServer, SocketGuild overflowServer, CancellationToken cancellationToken) {
            foreach (var overflowUser in overflowServer.Users) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await WaitOnCoopsBeingCreated(cancellationToken);

                var mainServerUser = mainServer.Users.FirstOrDefault(x => x.Id == overflowUser.Id);
                if (mainServerUser == null)
                    continue;

                if (overflowUser.Nickname != mainServerUser.Nickname && !overflowUser.IsBot && overflowUser.Guild.OwnerId != overflowUser.Id) {
                    try {
                        _logger.LogInformation("Updating nickname for {userName} in {serverName}", mainServerUser.GetName(), overflowServer.Name);
                        await overflowUser.ModifyAsync(x => x.Nickname = mainServerUser.Nickname);
                    } catch (Exception ex) {
                        _logger.LogWarning(ex, "Unable to change nickname for {userName}", mainServerUser.GetName());
                    }
                    StillAlive();
                }
            }
        }

        /// <summary>
        /// Purge pending coop assignments for members who have left the guild.
        /// </summary>
        private async Task PurgePendingAssignments(ApplicationDbContext db, List<Guid> departedUserIds, ulong guildId) {
            if (departedUserIds.Count == 0)
                return;

            var staleXrefs = await db.UserCoopXrefs
                .Where(PendingAssignmentPurgeFilter(departedUserIds, guildId, DateTimeOffset.UtcNow))
                .Select(x => new { x.UserId, x.Coop.ContractID, Xref = x })
                .ToListAsync(CancellationToken.None);

            if (staleXrefs.Count == 0)
                return;

            db.UserCoopXrefs.RemoveRange(staleXrefs.Select(x => x.Xref));
            var lookup = _provider.GetService<CoopAssignmentLookup>();
            foreach (var stale in staleXrefs) {
                lookup?.Remove(stale.UserId, stale.ContractID);
                _logger.LogInformation("Purged pending coop assignment for departed user {userId}", stale.UserId);
                StillAlive();
            }
        }

        /// <summary>
        /// Filter for pending coop assignments that should be purged.
        /// </summary>
        public static Expression<Func<UserCoopXref, bool>> PendingAssignmentPurgeFilter(List<Guid> departedUserIds, ulong guildId, DateTimeOffset now) =>
            x => departedUserIds.Contains(x.UserId)
              && !x.JoinedCoop
              && x.Coop.GuildId == guildId
              && (int)x.Coop.Status > 2 && (int)x.Coop.Status < 13
              && x.Coop.CoopEnds > now && !x.Coop.PseudoExpired;

        /// <summary>
        /// Handle role synchronization when a role is updated in real-time.
        /// </summary>
        private Task _client_RoleUpdated(SocketRole originalRole, SocketRole updatedRole) {
            _ = Task.Run(async () => {
                await OverflowSyncing.SyncRoleUpdateAsync(originalRole, updatedRole, _provider, _logger);
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Sync channel permissions from main to overflow servers.
        /// </summary>
        private async Task HandleChannelPermissionSyncs(SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            // Implementation existing channel sync logic here
            // This method should remain in ManageOverflow as it's specific to channel management
            await Task.CompletedTask;
        }

        ///// <summary>
        ///// Sync application command permissions from main to overflow servers.
        ///// </summary>
        //private async Task HandleApplicationCommandPermissionSyncs(Guild guild, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, CancellationToken cancellationToken) {
        //    try {
        //        if (guild.RolesToSync is null || string.IsNullOrWhiteSpace(guild.RolesToSync)) {
        //            return;
        //        }

        //        _logger.LogInformation("Syncing application command permissions for {guildName}", guild.Name);

        //        var rolesToSync = guild.RolesToSync.Split(",")
        //            .Select(roleIdStr =>(IRole)mainServer.Roles.FirstOrDefault(x => x.Id.ToString() == roleIdStr))
        //            .Where(x => x != null)
        //            .ToList();

        //        if (rolesToSync.Count == 0) {
        //            _logger.LogWarning("No roles found to sync for {guildName}", guild.Name);
        //            return;
        //        }

        //        var roleMaps = OverflowSyncing.GetRoleMaps(rolesToSync, overflowServers);
        //        // Pass the client to the syncing method
        //        var syncResult = await OverflowSyncing.HandleCommandPermissionSyncsAsync(_client.Gateway, mainServer, overflowServers, roleMaps);

        //        if (!string.IsNullOrEmpty(syncResult)) {
        //            _logger.LogInformation("Command permission sync result:\n{result}", syncResult);
        //        }

        //        StillAlive();
        //    } catch (Exception ex) {
        //        _logger.LogError(ex, "Error syncing application command permissions for {guildName}", guild.Name);
        //    }
        //}
    }
}