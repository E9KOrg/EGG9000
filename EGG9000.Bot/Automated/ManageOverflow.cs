using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class ManageOverflow(IServiceProvider provider) : _UpdaterBase<ManageOverflow>(TimeSpan.FromMinutes(5.6), TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);

            var users = await _db.DBUsers.Select(x => new { x.Id, x.DiscordId, x.GuildId, x.LastGuild }).ToListAsync(CancellationToken.None);
            // Partition once so each guild is an O(1) lookup instead of re-scanning every user per guild.
            var usersByGuild = users.Where(x => x.GuildId != 0).ToLookup(x => x.GuildId);
            var departedByLastGuild = users.Where(x => x.GuildId == 0).ToLookup(x => x.LastGuild);
            foreach(var guild in guilds) {
                if(_client.Guilds.FirstOrDefault(x => x.Id == guild.DiscordSeverId) is not
                    { } mainServer)
                    continue;
                await mainServer.DownloadUsersAsync();

                var missingFromServer = usersByGuild[guild.Id].Where(x => mainServer.GetUser(x.DiscordId) is null).Select(x => x.Id).ToList();

                var membersMissing = await _db.DBUsers.Where(x => missingFromServer.Contains(x.Id)).ToListAsync(CancellationToken.None);
                membersMissing.ForEach(x => {
                    x.GuildId = 0; x.LastGuild = guild.Id;
                    _logger.LogInformation("Removing member from the guild {name}", x.DiscordUsername);
                    StillAlive();
                });

                var returned = departedByLastGuild[guild.Id].Where(x => mainServer.GetUser(x.DiscordId) is not null).Select(x => x.Id).ToList();
                var membersReturn = await _db.DBUsers.Where(x => returned.Contains(x.Id)).ToListAsync(CancellationToken.None);
                membersReturn.ForEach(x => {
                    x.GuildId = guild.Id;
                    _logger.LogInformation("Return member to the guild {name}", x.DiscordUsername);
                    StillAlive();
                });

                await _db.SaveChangesAsync(CancellationToken.None);
                StillAlive();
            }


            // Subscribe once per run; the -=/+= guarantees a single handler instead of accumulating one per overflow guild on every run.
            _client.Gateway.RoleUpdated -= _client_RoleUpdated;
            _client.Gateway.RoleUpdated += _client_RoleUpdated;

            foreach(var guild in guilds.Where(x => x.OverflowServers.Count > 0)) {
                if(cancellationToken.IsCancellationRequested) break;
                _logger.LogInformation("Manage Overflow for {guildName}", guild.Name);
                var mainServer = _client.Guilds.First(x => x.Id == guild.DiscordSeverId);
                var overflowServers = _client.Guilds.Where(x => guild.OverflowServers.Contains(x.Id)).ToList();
                await mainServer.DownloadUsersAsync();
                foreach(var server in overflowServers) {
                    await server.DownloadUsersAsync();
                }
                await HandleChannelPermissionSyncs(mainServer, overflowServers, cancellationToken);
                await HandleRoleSyncs(guild, mainServer, overflowServers, cancellationToken);

                const ulong overflowRoleID = 775547850134257675;
                const ulong registeredRoleID = 794713762396897280;


                var onlyMain = mainServer.Users.Where(x => !x.IsBot && !overflowServers.All(o => o.GetUser(x.Id) is not null));
                var allOverflows = mainServer.Users.Where(x => !x.IsBot && (overflowServers.All(o => o.GetUser(x.Id) is not null) || !x.Roles.Any(y => y.Id == registeredRoleID)));

                var bothAllWithRole = allOverflows.Where(x => x.Roles.Any(y => y.Id == overflowRoleID));

                var onlyMainWithoutRole = onlyMain.Where(x => !x.Roles.Any(y => y.Id == overflowRoleID) && x.Roles.Count > 2 && x.Roles.Any(y => y.Id == registeredRoleID));

                if(mainServer.Roles.FirstOrDefault(x => x.Id == overflowRoleID) is not
                    { } role) {
                    _logger.LogWarning("Unable to find overflow role for {guild}", guild.Name);
                    continue;
                }


               foreach(var u in onlyMainWithoutRole) {
                    await WaitOnCoopsBeingCreated(cancellationToken);
                    if(cancellationToken.IsCancellationRequested) break;
                    await u.AddRoleAsync(role);
                    _logger.LogInformation("Adding overflow role for {user}", u.GetName());
                    StillAlive();
                }

                foreach(var u in mainServer.Users.Where(x => x.Roles.Count == 1 && x.Roles.Any(y => y.Id == overflowRoleID) && !x.IsBot)) {
                    await WaitOnCoopsBeingCreated(cancellationToken);
                    if(cancellationToken.IsCancellationRequested) break;
                    await u.RemoveRoleAsync(role);
                    _logger.LogInformation("Removing overflow role for {user}, it was the only role", u.GetName());
                    StillAlive();
                }

                foreach(var u in bothAllWithRole) {
                    await WaitOnCoopsBeingCreated(cancellationToken);
                    if(cancellationToken.IsCancellationRequested) break;
                    await u.RemoveRoleAsync(role);
                    _logger.LogInformation("Removing overflow role for {user}, they were in all servers.", u.GetName());
                    StillAlive();
                }


                foreach(var overflowServer in overflowServers) {
                    await WaitOnCoopsBeingCreated(cancellationToken);
                    if(cancellationToken.IsCancellationRequested) break;
                    var onlyOverflow = overflowServer.Users.Where(x => !x.IsBot && mainServer.GetUser(x.Id) is null);
                    foreach(var u in onlyOverflow) {
                        await u.KickAsync("No longer in main server");
                        _logger.LogInformation("Kicking {user}, no longer in main server", u.GetName());
                        StillAlive();
                    }

                    foreach(var overflowUser in overflowServer.Users) {
                        await WaitOnCoopsBeingCreated(cancellationToken);
                        if(cancellationToken.IsCancellationRequested) break;
                        if(mainServer.GetUser(overflowUser.Id) is not { } mainServerUser)
                            continue;
                        if(overflowUser.Nickname != mainServerUser.Nickname && !overflowUser.IsBot && overflowUser.Guild.OwnerId != overflowUser.Id) {
                            try {
                                _logger.LogInformation("Changing nickname for {newName}, it was {currentName} in {overflow}", mainServerUser.Nickname, overflowUser.Nickname, overflowServer.Name);
                                await overflowUser.ModifyAsync(x => x.Nickname = mainServerUser.Nickname);
                            } catch(Exception) {
                                _logger.LogWarning("Unable to change nickname for {user}", mainServerUser.GetName());
                            }
                            StillAlive();
                        }
                    }
                }
                StillAlive();
            }
        }

        private Task _client_RoleUpdated(SocketRole originalRole, SocketRole updatedRole) {
            _ = Task.Run(async () => await UpdateRoles(originalRole, updatedRole));
            return Task.CompletedTask;
        }

        private async Task UpdateRoles(SocketRole originalRole, SocketRole updatedRole) {
            if(originalRole?.Guild?.Id == default)
                return;

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guild = await _db.Guilds.FirstOrDefaultAsync(x => x.Id == originalRole.Guild.Id);

            if(guild is null || guild.OverflowServers.Count == 0 || guild.RolesToSync is null || !guild.RolesToSync.Contains(originalRole.Id.ToString()))
                return;

            var overflowServers = _client.Guilds.Where(x => guild.OverflowServers.Contains(x.Id));
            foreach(var overflowServer in overflowServers) {
                if(overflowServer.Roles.FirstOrDefault(x => x.Name == originalRole.Name) is not
                    { } overflowRole)
                    continue;
                try {
                    await overflowRole.ModifyAsync(async x => {
                        x.Name = updatedRole.Name;
                        x.Colors = updatedRole.Colors;
                        x.Permissions = updatedRole.Permissions;
                        if(updatedRole.Icon != originalRole.Icon && overflowServer.Features.HasFeature(GuildFeature.RoleIcons)) {
                            x.Icon = new Image(await DownloadImage(updatedRole.GetIconUrl()));
                        }
                    }, new RequestOptions() { RetryMode = RetryMode.RetryRatelimit });
                } catch(Exception) { }
                StillAlive();
            }
        }

        private static async Task<Stream> DownloadImage(string url) {
            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(url);
            return new MemoryStream(bytes);
        }

        private async Task HandleRoleSyncs(Guild guild, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            if(guild.RolesToSync is null)
                return;
            var roleids = guild.RolesToSync.Split(",").ToHashSet();
            var rolesToSync = mainServer.Roles.Where(x => roleids.Contains(x.Id.ToString())).ToList();

            foreach(var overflowServer in overflowServers) {
                StillAlive();
                if(cancellationToken.IsCancellationRequested) break;

                //Add missing roles
                foreach(var role in rolesToSync.OrderByDescending(x => x.Position)) {
                    if(cancellationToken.IsCancellationRequested) break;

                    IRole overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                    if(overflowRole is null) {
                        overflowRole = await overflowServer.CreateRoleAsync(role.Name, color: role.Colors);
                    } else if(overflowServer.Features.HasFeature(GuildFeature.RoleIcons) && string.IsNullOrEmpty(overflowRole.Icon) && !string.IsNullOrEmpty(role.Icon)) {
                        var image = new Image(await DownloadImage(role.GetIconUrl()));
                        await overflowRole.ModifyAsync(x => x.Icon = image);
                    }
                    else if(!role.Permissions.Equals(overflowRole.Permissions)) {
                        await overflowRole.ModifyAsync(x => {
                            x.Name = role.Name;
                            x.Colors = role.Colors;
                            x.Permissions = role.Permissions;
                        });
                    }
                }

                //Sync user roles. Build a name->role lookup once instead of scanning all server roles per user/per role.
                var overflowRolesByName = overflowServer.Roles
                    .GroupBy(x => x.Name)
                    .ToDictionary(g => g.Key, g => g.First());
                foreach(var overflowUser in overflowServer.Users.ToList()) {
                    StillAlive();
                    if(cancellationToken.IsCancellationRequested) break;
                    if(mainServer.GetUser(overflowUser.Id) is not
                        { } mainServerUser)
                        continue;

                    var mainRoleNames = mainServerUser.Roles.Select(x => x.Name).ToHashSet();
                    var overflowRoleNames = overflowUser.Roles.Select(x => x.Name).ToHashSet();

                    var neededRoles = new List<SocketRole>();
                    var removeRoles = new List<SocketRole>();
                    foreach(var role in rolesToSync) {
                        var hasRoleInMain = mainRoleNames.Contains(role.Name);
                        var hasRoleInOverflow = overflowRoleNames.Contains(role.Name);
                        if(hasRoleInMain == hasRoleInOverflow || overflowRolesByName.GetValueOrDefault(role.Name) is not
                            { } overflowRole)
                            continue;
                        if(hasRoleInMain) neededRoles.Add(overflowRole);
                        else removeRoles.Add(overflowRole);
                    }
                    if(neededRoles.Count > 0) {
                        _logger.LogInformation("Adding overflow roles ({roles}) to {user} in {overflowServer}",
                            string.Join(",", neededRoles.Select(x => x.Name)), overflowUser.GetCleanName(), overflowServer.Name);
                        await overflowUser.AddRolesAsync(neededRoles);
                    }
                    if(removeRoles.Count > 0) {
                        _logger.LogInformation("Removing overflow roles ({roles}) to {user}", string.Join(",", removeRoles.Select(x => x.Name)), overflowUser.GetCleanName());
                        await overflowUser.RemoveRolesAsync(removeRoles);
                    }
                }

            }
        }

        private async Task HandleChannelPermissionSyncs(SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            var mainCoopCategory = (await _client.GetAllCoopCategories(mainServer)).First();

            foreach(var overflowServer in overflowServers) {
                if(cancellationToken.IsCancellationRequested) { continue; }
                var matches = mainCoopCategory.PermissionOverwrites
                    .Where(y => y.TargetType == PermissionTarget.Role)
                    .Select(x => {
                        var mainRole = mainServer.GetRole(x.TargetId);
                        return new OverflowPermissionRoleMatch {
                            MainRole = mainRole,
                            OverflowRole = mainRole is null ? null : overflowServer.Roles.FirstOrDefault(r => r.Name == mainRole.Name),
                            Overwrite = x
                        };
                    }).ToList();
                var matchesByOverflowRoleId = matches
                    .Where(m => m.OverflowRole is not null)
                    .GroupBy(m => m.OverflowRole.Id)
                    .ToDictionary(g => g.Key, g => g.First());

                var coopCategories = await _client.GetAllCoopCategories(overflowServer);
                foreach(var coopCategory in coopCategories) {
                    if(cancellationToken.IsCancellationRequested) { continue; }
                    foreach(var overwrite in coopCategory.PermissionOverwrites) {
                        if(cancellationToken.IsCancellationRequested) { continue; }
                        StillAlive();
                        if(matchesByOverflowRoleId.GetValueOrDefault(overwrite.TargetId) is not
                            { } match) {
                            if(overwrite.TargetType == PermissionTarget.Role) {
                                await coopCategory.RemovePermissionOverwriteAsync(overflowServer.GetRole(overwrite.TargetId));
                            } else {
                                await coopCategory.RemovePermissionOverwriteAsync(overflowServer.GetUser(overwrite.TargetId));
                            }

                        } else if(!Compare(overwrite.Permissions, match.Overwrite.Permissions)) {
                            await coopCategory.AddPermissionOverwriteAsync(match.OverflowRole, match.Overwrite.Permissions);
                        }
                    }

                    var categoryOverwriteIds = coopCategory.PermissionOverwrites.Select(y => y.TargetId).ToHashSet();
                    foreach(var match in matches.Where(m => m.OverflowRole is not null && !categoryOverwriteIds.Contains(m.OverflowRole.Id))) {
                        if(cancellationToken.IsCancellationRequested) { continue; }
                        StillAlive();
                        await coopCategory.AddPermissionOverwriteAsync(match.OverflowRole, match.Overwrite.Permissions);
                    }
                }
            }
        }
        public static bool Compare(OverwritePermissions x, OverwritePermissions y) {
            return (
              from l1 in x.GetType().GetFields()
              join l2 in y.GetType().GetFields() on l1.Name equals l2.Name
              where !l1.GetValue(x).Equals(l2.GetValue(y))
              select l1.GetValue(x) == l2.GetValue(y)
            ).All(x => x);
        }

        private class OverflowPermissionRoleMatch {
            public SocketRole MainRole { get; set; }
            public SocketRole OverflowRole { get; set; }
            public Overwrite Overwrite { get; set; }
        }

    }
}