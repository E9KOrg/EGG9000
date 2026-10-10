using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers.Discord {
    public static class OverflowSyncing {
        private const int RolePermission = 1;
        private const int UserPermission = 2;
        private const int ChannelPermission = 3;

        public static string[] ParseRoleIds(Guild guild) =>
            (guild?.RolesToSync ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static List<SocketRole> GetRolesToSync(Guild guild, SocketGuild mainServer) {
            var roleIds = ParseRoleIds(guild);
            return [.. mainServer.Roles.Where(x => roleIds.Contains(x.Id.ToString()))];
        }

        public static async Task HandleRoleSyncsAsync(Guild guild, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, ILogger logger, CancellationToken cancellationToken) {
            var rolesToSync = GetRolesToSync(guild, mainServer);
            if(rolesToSync.Count == 0)
                return;

            foreach(var overflowServer in overflowServers) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                await SyncRolesToOverflowServerAsync(overflowServer, rolesToSync, logger, cancellationToken);
                await SyncRoleMembershipsAsync(mainServer, overflowServer, rolesToSync, logger, cancellationToken);
            }
        }

        private static async Task SyncRolesToOverflowServerAsync(SocketGuild overflowServer, IEnumerable<SocketRole> rolesToSync, ILogger logger, CancellationToken cancellationToken) {
            foreach(var role in rolesToSync.OrderByDescending(x => x.Position)) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                var syncColors = overflowServer.Features.HasEnhancedRoleColors
                    ? role.Colors
                    : RoleColors.Solid(role.Colors.PrimaryColor);

                if(overflowRole is null) {
                    await overflowServer.CreateRoleAsync(role.Name, permissions: role.Permissions, color: syncColors);
                    logger?.LogInformation("Created role {RoleName} in {ServerName}", role.Name, overflowServer.Name);
                } else if(!role.Permissions.Equals(overflowRole.Permissions) || overflowRole.Colors.PrimaryColor != role.Colors.PrimaryColor) {
                    await overflowRole.ModifyAsync(x => {
                        x.Name = role.Name;
                        x.Colors = syncColors;
                        x.Permissions = role.Permissions;
                    });
                    logger?.LogInformation("Updated role {RoleName} in {ServerName}", role.Name, overflowServer.Name);
                }
            }
        }

        private static async Task SyncRoleMembershipsAsync(SocketGuild mainServer, SocketGuild overflowServer, IEnumerable<SocketRole> rolesToSync, ILogger logger, CancellationToken cancellationToken) {
            foreach(var overflowUser in overflowServer.Users.ToArray()) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                var mainServerUser = mainServer.GetUser(overflowUser.Id);
                if(mainServerUser is null)
                    continue;

                var rolesToAdd = new List<IRole>();
                var rolesToRemove = new List<IRole>();

                foreach(var role in rolesToSync) {
                    var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                    if(overflowRole is null)
                        continue;

                    var hasRoleInMain = mainServerUser.Roles.Any(x => x.Name == role.Name);
                    var hasRoleInOverflow = overflowUser.Roles.Any(x => x.Id == overflowRole.Id);

                    if(hasRoleInMain && !hasRoleInOverflow)
                        rolesToAdd.Add(overflowRole);
                    else if(!hasRoleInMain && hasRoleInOverflow)
                        rolesToRemove.Add(overflowRole);
                }

                if(rolesToAdd.Count > 0) {
                    await overflowUser.AddRolesAsync(rolesToAdd);
                    logger?.LogInformation("Added roles ({Roles}) to {UserName} in {ServerName}", string.Join(",", rolesToAdd.Select(x => x.Name)), mainServerUser.GetCleanName(), overflowServer.Name);
                }

                if(rolesToRemove.Count > 0) {
                    await overflowUser.RemoveRolesAsync(rolesToRemove);
                    logger?.LogInformation("Removed roles ({Roles}) from {UserName} in {ServerName}", string.Join(",", rolesToRemove.Select(x => x.Name)), mainServerUser.GetCleanName(), overflowServer.Name);
                }
            }
        }

        public static async Task SyncRoleUpdateAsync(SocketRole originalRole, SocketRole updatedRole, ApplicationDbContext db, DiscordHostedService client, ILogger logger) {
            if(originalRole?.Guild is null)
                return;

            var guild = await db.Guilds.FirstOrDefaultAsync(x => x.DiscordSeverId == originalRole.Guild.Id);
            if(guild is null || guild.OverflowServers.Count == 0)
                return;

            if(!ParseRoleIds(guild).Contains(originalRole.Id.ToString()))
                return;

            foreach(var overflowServer in client.Guilds.Where(x => guild.OverflowServers.Contains(x.Id))) {
                var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == originalRole.Name);
                if(overflowRole is null)
                    continue;

                var syncColors = overflowServer.Features.HasEnhancedRoleColors
                    ? updatedRole.Colors
                    : RoleColors.Solid(updatedRole.Colors.PrimaryColor);

                try {
                    await overflowRole.ModifyAsync(x => {
                        x.Name = updatedRole.Name;
                        x.Colors = syncColors;
                        x.Permissions = updatedRole.Permissions;
                    }, new RequestOptions { RetryMode = RetryMode.RetryRatelimit });
                    logger?.LogInformation("Synced role update {RoleName} to {ServerName}", updatedRole.Name, overflowServer.Name);
                } catch(Exception ex) {
                    logger?.LogWarning(ex, "Failed to sync role update {RoleName} to {ServerName}", updatedRole.Name, overflowServer.Name);
                }
            }
        }

        public static async Task HandleChannelPermissionSyncsAsync(DiscordHostedService client, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, ILogger logger, CancellationToken cancellationToken) {
            var mainCoopCategory = (await client.GetAllCoopCategories(mainServer)).FirstOrDefault();
            if(mainCoopCategory is null) {
                logger?.LogWarning("No coop category configured for {ServerName}, skipping channel permission sync", mainServer.Name);
                return;
            }

            var mainRoleOverwrites = mainCoopCategory.PermissionOverwrites.Where(x => x.TargetType == PermissionTarget.Role).ToList();

            foreach(var overflowServer in overflowServers) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                var desired = mainRoleOverwrites
                    .Select(x => (x.Permissions, OverflowRole: overflowServer.Roles.FirstOrDefault(r => r.Name == mainServer.GetRole(x.TargetId)?.Name)))
                    .Where(x => x.OverflowRole is not null)
                    .ToList();

                foreach(var category in await client.GetAllCoopCategories(overflowServer)) {
                    if(cancellationToken.IsCancellationRequested)
                        break;

                    foreach(var overwrite in category.PermissionOverwrites.ToList()) {
                        var match = desired.FirstOrDefault(x => x.OverflowRole.Id == overwrite.TargetId);
                        if(match.OverflowRole is null) {
                            await RemoveOverwriteAsync(overflowServer, category, overwrite);
                            logger?.LogInformation("Removed stray {TargetType} overwrite {TargetId} from {Category} in {ServerName}", overwrite.TargetType, overwrite.TargetId, category.Name, overflowServer.Name);
                        } else if(!OverwritesMatch(overwrite.Permissions, match.Permissions)) {
                            await category.AddPermissionOverwriteAsync(match.OverflowRole, match.Permissions);
                            logger?.LogInformation("Updated overwrite for {RoleName} on {Category} in {ServerName}", match.OverflowRole.Name, category.Name, overflowServer.Name);
                        }
                    }

                    foreach(var (permissions, overflowRole) in desired.Where(x => !category.PermissionOverwrites.Any(y => y.TargetId == x.OverflowRole.Id))) {
                        await category.AddPermissionOverwriteAsync(overflowRole, permissions);
                        logger?.LogInformation("Added overwrite for {RoleName} on {Category} in {ServerName}", overflowRole.Name, category.Name, overflowServer.Name);
                    }
                }
            }
        }

        private static Task RemoveOverwriteAsync(SocketGuild guild, SocketCategoryChannel category, Overwrite overwrite) {
            if(overwrite.TargetType == PermissionTarget.Role)
                return guild.GetRole(overwrite.TargetId) is { } role ? category.RemovePermissionOverwriteAsync(role) : Task.CompletedTask;
            return guild.GetUser(overwrite.TargetId) is { } user ? category.RemovePermissionOverwriteAsync(user) : Task.CompletedTask;
        }

        public static bool OverwritesMatch(OverwritePermissions x, OverwritePermissions y) =>
            x.AllowValue == y.AllowValue && x.DenyValue == y.DenyValue;

        public static async Task<string> HandleCommandPermissionSyncsAsync(DiscordSocketClient client, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, List<RoleMap> roleMaps, string accessToken) {
            var sb = new StringBuilder();
            using var restClient = new DiscordRestApiClient(client);

            var commands = await client.Rest.GetGlobalApplicationCommands();
            var mainPermissions = await restClient.GetApplicationCommandPermissionsAsync(mainServer.Id);

            var overflowList = overflowServers.ToList();
            var overflowPermissions = new Dictionary<ulong, List<GuildApplicationCommandPermissionRest>>();
            foreach(var overflowServer in overflowList)
                overflowPermissions[overflowServer.Id] = await restClient.GetApplicationCommandPermissionsAsync(overflowServer.Id);

            foreach(var command in commands) {
                var permissions = mainPermissions.FirstOrDefault(x => x.Id == command.Id);
                if(permissions?.Permissions is not { Count: > 0 })
                    continue;

                foreach(var overflowServer in overflowList) {
                    try {
                        var current = overflowPermissions[overflowServer.Id].FirstOrDefault(x => x.Id == command.Id)?.Permissions;
                        var mapped = MapPermissionsToOverflow(permissions.Permissions, mainServer.Id, overflowServer.Id, roleMaps);

                        if(PermissionsMatch(mapped, current)) {
                            sb.AppendLine($"Skipped /{command.Name} in {overflowServer.Name} (unchanged)");
                            continue;
                        }

                        await restClient.EditApplicationCommandPermissionsAsync(overflowServer.Id, command.Id, mapped, accessToken);
                        await Task.Delay(500);
                        sb.AppendLine($"Updated /{command.Name} in {overflowServer.Name}");
                    } catch(Exception ex) {
                        sb.AppendLine($"ERROR /{command.Name} in {overflowServer.Name}: {ex.Message}");
                    }
                }
            }

            return sb.ToString();
        }

        public static List<CommandPermissionRest> MapPermissionsToOverflow(IReadOnlyCollection<CommandPermissionRest> permissions, ulong mainGuildId, ulong overflowGuildId, List<RoleMap> roleMaps) {
            var mapped = new List<CommandPermissionRest>();
            foreach(var permission in permissions) {
                switch(permission.Type) {
                    case RolePermission when permission.Id == mainGuildId:
                        mapped.Add(new CommandPermissionRest { Id = overflowGuildId, Type = RolePermission, Permission = permission.Permission });
                        break;
                    case RolePermission:
                        var mapping = roleMaps.FirstOrDefault(x => x.RoleID == permission.Id)?.Values.FirstOrDefault(x => x.GuildId == overflowGuildId);
                        if(mapping is { RoleId: not 0 })
                            mapped.Add(new CommandPermissionRest { Id = mapping.Value.RoleId, Type = RolePermission, Permission = permission.Permission });
                        break;
                    case UserPermission:
                        mapped.Add(permission);
                        break;
                    case ChannelPermission:
                        break;
                }
            }
            return mapped;
        }

        public static bool PermissionsMatch(IReadOnlyCollection<CommandPermissionRest> desired, IReadOnlyCollection<CommandPermissionRest> current) {
            if(desired is null || current is null)
                return desired is null && current is null;
            if(desired.Count != current.Count)
                return false;
            return desired.All(d => current.Any(c => c.Type == d.Type && c.Id == d.Id && c.Permission == d.Permission));
        }

        public static async Task<bool> IsMissingFromAnyOverflowAsync(Guild guild, DiscordHostedService client, ulong userId) {
            if(guild is null)
                return true;
            if(guild.OverflowServers.Count == 0)
                return false;

            foreach(var overflowId in guild.OverflowServers) {
                var overflowServer = client.GetGuild(overflowId);
                if(overflowServer is null)
                    return true;
                if(overflowServer.GetUser(userId) is not null)
                    continue;
                try {
                    if(await client.Rest.GetGuildUserAsync(overflowId, userId) is null)
                        return true;
                } catch {
                    return true;
                }
            }
            return false;
        }

        public static List<RoleMap> GetRoleMaps(IEnumerable<IRole> rolesToSync, IEnumerable<SocketGuild> overflowServers) {
            var roles = rolesToSync.ToList();
            var roleMaps = roles.Select(x => new RoleMap { RoleID = x.Id, Values = [] }).ToList();

            foreach(var overflowServer in overflowServers) {
                foreach(var role in roles) {
                    var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                    if(overflowRole is not null)
                        roleMaps.First(x => x.RoleID == role.Id).Values.Add((overflowServer.Id, overflowRole.Id));
                }
            }
            return roleMaps;
        }
    }

    public class RoleMap {
        public ulong RoleID { get; set; }
        public List<(ulong GuildId, ulong RoleId)> Values { get; set; }
    }
}
