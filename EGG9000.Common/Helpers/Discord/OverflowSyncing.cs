using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers.Discord {
    public class OverflowSyncing {

        /// <summary>
        /// Sync role configurations from main guild to overflow guilds.
        /// </summary>
        public static async Task HandleRoleSyncsAsync(Guild guild, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, IServiceProvider provider, ILogger logger, CancellationToken cancellationToken) {
            if (guild.RolesToSync is null)
                return;

            var roleIds = guild.RolesToSync.Split(",");
            var rolesToSync = mainServer.Roles.Where(x => roleIds.Any(y => y == x.Id.ToString()));

            foreach (var overflowServer in overflowServers) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Create or update roles
                await SyncRolesToOverflowServerAsync(mainServer, overflowServer, rolesToSync, logger, cancellationToken);

                // Assign roles to members
                await SyncRoleMembershipsAsync(mainServer, overflowServer, rolesToSync, logger, cancellationToken);
            }
        }

        /// <summary>
        /// Create or update roles in an overflow server to match the main server.
        /// </summary>
        private static async Task SyncRolesToOverflowServerAsync(SocketGuild mainServer, SocketGuild overflowServer, IEnumerable<IRole> rolesToSync, ILogger logger, CancellationToken cancellationToken) {
            foreach (var role in rolesToSync.OrderByDescending(x => x.Position)) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                IRole overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                var syncColors = overflowServer.Features.HasEnhancedRoleColors
                    ? role.Colors
                    : RoleColors.Solid(role.Colors.PrimaryColor);

                if (overflowRole is null) {
                    overflowRole = await overflowServer.CreateRoleAsync(role.Name, color: syncColors);
                    logger?.LogInformation("Created role {roleName} in {serverName}", role.Name, overflowServer.Name);
                } else if (!role.Permissions.Equals(overflowRole.Permissions) || overflowRole.Color != role.Color) {
                    await overflowRole.ModifyAsync(x => {
                        x.Name = role.Name;
                        x.Colors = syncColors;
                        x.Permissions = role.Permissions;
                    });
                    logger?.LogInformation("Updated role {roleName} in {serverName}", role.Name, overflowServer.Name);
                }
            }
        }

        /// <summary>
        /// Sync role memberships for users across main and overflow servers.
        /// </summary>
        private static async Task SyncRoleMembershipsAsync(SocketGuild mainServer, SocketGuild overflowServer, IEnumerable<IRole> rolesToSync, ILogger logger, CancellationToken cancellationToken) {
            for (var i = 0; i < overflowServer.Users.Count; i++) {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var overflowUser = overflowServer.Users.ElementAt(i);
                var mainServerUser = mainServer.Users.FirstOrDefault(x => x.Id == overflowUser.Id);

                if (mainServerUser == null)
                    continue;

                var rolesToAdd = new List<IRole>();
                var rolesToRemove = new List<IRole>();

                foreach (var role in rolesToSync) {
                    var hasRoleInMain = mainServerUser.Roles.Any(x => x.Name == role.Name);
                    var hasRoleInOverflow = overflowUser.Roles.Any(x => x.Name == role.Name);
                    var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);

                    if (hasRoleInMain && !hasRoleInOverflow && overflowRole is not null) {
                        rolesToAdd.Add(overflowRole);
                    } else if (!hasRoleInMain && hasRoleInOverflow && overflowRole is not null) {
                        rolesToRemove.Add(overflowRole);
                    }
                }

                if (rolesToAdd.Count > 0) {
                    await overflowUser.AddRolesAsync(rolesToAdd);
                    logger?.LogInformation("Added {roleCount} roles to {userName} in {serverName}", rolesToAdd.Count, mainServerUser.GetName(), overflowServer.Name);
                }

                if (rolesToRemove.Count > 0) {
                    await overflowUser.RemoveRolesAsync(rolesToRemove);
                    logger?.LogInformation("Removed {roleCount} roles from {userName} in {serverName}", rolesToRemove.Count, mainServerUser.GetName(), overflowServer.Name);
                }
            }
        }

        /// <summary>
        /// Sync a role update from main guild to overflow guilds in real-time.
        /// </summary>
        public static async Task SyncRoleUpdateAsync(SocketRole originalRole, SocketRole updatedRole, IServiceProvider provider, ILogger logger) {
            if (originalRole?.Guild?.Id == default)
                return;

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == originalRole.Guild.Id);

            if (guild is null || guild.OverflowServers.Count == 0 || guild.RolesToSync is null || !guild.RolesToSync.Contains(originalRole.Id.ToString()))
                return;

            var client = provider.GetService<DiscordHostedService>();
            var overflowServers = client?.Guilds.Where(x => guild.OverflowServers.Contains(x.Id));

            if (overflowServers == null)
                return;

            foreach (var overflowServer in overflowServers) {
                var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == originalRole.Name);
                if (overflowRole != null) {
                    var syncColors = overflowServer.Features.HasEnhancedRoleColors
                        ? updatedRole.Colors
                        : RoleColors.Solid(updatedRole.Colors.PrimaryColor);

                    try {
                        await overflowRole.ModifyAsync(x => {
                            x.Name = updatedRole.Name;
                            x.Colors = syncColors;
                            x.Permissions = updatedRole.Permissions;
                        }, new RequestOptions() { RetryMode = RetryMode.RetryRatelimit });

                        logger?.LogInformation("Synced role update {roleName} to {serverName}", updatedRole.Name, overflowServer.Name);
                    } catch (Exception ex) {
                        logger?.LogWarning(ex, "Failed to sync role update {roleName} to {serverName}", updatedRole.Name, overflowServer.Name);
                    }
                }
            }
        }

        /// <summary>
        /// Sync application command permissions from the main guild to overflow guilds.
        /// </summary>
        public static async Task<string> HandleCommandPermissionSyncsAsync(DiscordSocketClient client, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, List<RoleMap> roleMaps, string accessToken) {
            var sb = new StringBuilder();
            
            // Use the client directly instead of trying to extract from the guild
            using var restClient = new DiscordRestApiClient(client);

            var commands = await client.Rest.GetGlobalApplicationCommands();
            var allPermissions = await restClient.GetApplicationCommandPermissionsAsync(mainServer.Id);

            var allOverflowPermissions = overflowServers.Select(x => new { Overflow = x, Permissions =  restClient.GetApplicationCommandPermissionsAsync(x.Id).Result }).ToList();

            foreach(var command in commands) {
                try {
                    var permissions = allPermissions.FirstOrDefault(x => x.Id == command.Id);
                    if (permissions?.Permissions is null || permissions.Permissions.Count == 0)
                        continue;

                    foreach (var overflowServer in overflowServers) {
                        var currentOverflowPermissions = allOverflowPermissions.FirstOrDefault(x => x.Overflow.Id == overflowServer.Id)?.Permissions.FirstOrDefault(x => x.Id == command.Id)?.Permissions;
                        
                        var mappedPermissions = MapPermissionsToOverflow(permissions.Permissions, mainServer, overflowServer, roleMaps);

                        var match = PermissionsMatch(mappedPermissions, currentOverflowPermissions);
                        if (!match) {
                            var payload = new { permissions = mappedPermissions };
                            await restClient.EditApplicationCommandPermissionsAsync(overflowServer.Id, command.Id, payload, accessToken);
                            await Task.Delay(500); // Small delay to avoid hitting rate limits
                            sb.AppendLine($"✓ Updated permissions for '{command.Name}' in {overflowServer.Name}");
                        } else {
                            sb.AppendLine($"• Skipped permissions for '{command.Name}' in {overflowServer.Name} (unchanged)");
                        }
                    }
                } catch (Exception ex) {
                    sb.AppendLine($"ERROR syncing permissions for command '{command.Name}': {ex.Message}");
                }
            }

            return sb.ToString(); 
        }
             

        private static List<CommandPermissionRest> MapPermissionsToOverflow(IReadOnlyCollection<CommandPermissionRest> permissions, SocketGuild mainServer, SocketGuild overflowServer, List<RoleMap> roleMaps) {
            var mappedPermissions = new List<CommandPermissionRest>();
            foreach(var permission in permissions) {
                switch(permission.Type) {
                    case 1:
                        var roleMap = roleMaps.FirstOrDefault(x => x.RoleID == permission.Id);
                        if(roleMap != null) {
                            var overflowRoleId = roleMap.Values.FirstOrDefault(x => x.GuildId == overflowServer.Id).RoleId;
                            mappedPermissions.Add(new CommandPermissionRest { Id = overflowRoleId, Type = permission.Type, Permission = permission.Permission });
                        }
                        break;
                    case 2:
                        mappedPermissions.Add(permission);
                        break;
                    case 3:
                        break;
                }
            }
            return mappedPermissions;
        }

        /// <summary>
        /// Compare two permission lists to see if they match.
        /// </summary>
        private static bool PermissionsMatch(List<CommandPermissionRest> desired, IReadOnlyCollection<CommandPermissionRest> current) {
            if (desired is null && current is null)
                return true;

            if (desired is null || current is null)
                return false;

            if (desired.Count != current.Count)
                return false;

            foreach (var permission in desired) {
                if (!current.Any(x => x.Type == permission.Type && x.Permission == permission.Permission && x.Id == permission.Id)) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Map roles from main guild to overflow guilds.
        /// </summary>
        public static List<RoleMap> GetRoleMaps(IList<IRole> rolesToSync, IEnumerable<SocketGuild> overflowServers) {
            var roleMaps = rolesToSync.Select(x => {
                var map = new RoleMap {
                    RoleID = x.Id,
                    Values = [],
                };
                return map;
            }).ToList();

            foreach (var overflowServer in overflowServers) {
                foreach (var role in rolesToSync.OrderByDescending(x => x.Position)) {
                    var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                    if (overflowRole != null) {
                        roleMaps.First(x => x.RoleID == role.Id).Values.Add((overflowServer.Id, overflowRole.Id));
                    }
                }
            }
            return roleMaps;
        }
    }    //public class Permission {
    //    public string Id { get; set; }
    //    public int Type { get; set; }
    //    public bool PermissionBool { get; set; }
    //}

    //public class GuildApplicationCommandPermissions {
    //    public string Id { get; set; }
    //    public string ApplicationId { get; set; }
    //    public string GuildId { get; set; }
    //    public List<Permission> Permissions { get; set; }
    //}



    public class RoleMap {
        public ulong RoleID { get; set; }
        public List<(ulong GuildId, ulong RoleId)> Values { get; set; }
    }
}
