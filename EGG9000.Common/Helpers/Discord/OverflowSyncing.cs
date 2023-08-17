using Discord;
using Discord.WebSocket;

using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers.Discord {
    public class OverflowSyncing {

        public static async Task<string> HandleCommandPermissionSyncsAsync(Guild guild, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, List<RoleMap> roleMaps, string user_access_token, string client_secret) {
            if(guild.RolesToSync is null)
                return "";

            var sb = new StringBuilder();
            var commands = await mainServer.GetApplicationCommandsAsync();

            var overflowCommands = overflowServers.SelectMany(x => x.GetApplicationCommandsAsync().Result);

            foreach(var command in commands) {
                var permissions = await ContractsAPI.DiscordRestGetBot<GuildApplicationCommandPermissions>($"applications/{514257192803893272}/guilds/{mainServer.Id}/commands/{command.Id}/permissions", client_secret);

                if(permissions.permissions is null)
                    continue;
                foreach(var overflowServer in overflowServers) {
                    var overflowPermissions = new GuildApplicationCommandPermissions {
                        permissions = new List<Permission>()
                    };

                    foreach(var p in permissions.permissions) {
                        var np = new Permission {
                            id = p.type == 1 && p.id != mainServer.EveryoneRole.Id.ToString() ? roleMaps.First(y => y.RoleID.ToString() == p.id).Values.First(y => y.GuildId == overflowServer.Id).RoleId.ToString() : p.id,
                            permission = p.permission,
                            type = p.type
                        };
                        overflowPermissions.permissions.Add(np);
                    }

                    var overflowCommand = overflowCommands.FirstOrDefault(x => x.Name == command.Name);

                    var currentOverflowPermissions = await ContractsAPI.DiscordRestGetBot<GuildApplicationCommandPermissions>($"applications/{514257192803893272}/guilds/{overflowServer.Id}/commands/{overflowCommand.Id}/permissions", client_secret);


                    var match = true;
                    if(currentOverflowPermissions.permissions is not null && overflowPermissions.permissions.Count == currentOverflowPermissions.permissions.Count) {
                        foreach(var permission in overflowPermissions.permissions) {
                            if(!currentOverflowPermissions.permissions.Any(x => x.id == permission.id && x.permission == permission.permission && x.type == permission.type)) {
                                match = false;
                                break;
                            }
                        }
                    } else {
                        match = false;
                    }

                    if(match == false) {
                        var response = await ContractsAPI.DiscordRestPutUser<GuildApplicationCommandPermissions, GuildApplicationCommandPermissions>($"applications/{514257192803893272}/guilds/{overflowServer.Id}/commands/{overflowCommand.Id}/permissions", user_access_token, overflowPermissions);
                        sb.AppendLine("Permissions for " + command.Name + " updated for " + overflowServer.Name);
                    }
                }
            }
            await mainServer.GetApplicationCommandsAsync();

            return sb.ToString();
        }

        public static async Task<List<RoleMap>> GetRoleMaps(IList<SocketRole> rolesToSync, IEnumerable<SocketGuild> overflowServers) {
            var roleMaps = rolesToSync.Select(x => {
                var map = new RoleMap {
                    RoleID = x.Id,
                    Values = new List<(ulong GuildId, ulong RoleId)>()
                };
                return map;
            }).ToList();

            foreach(var overflowServer in overflowServers) {
                foreach(var role in rolesToSync.OrderByDescending(x => x.Position)) {
                    IRole overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                    roleMaps.First(x => x.RoleID == role.Id).Values.Add((overflowServer.Id, overflowRole.Id));
                }
            }
            return roleMaps;
        }
    }

    public class Permission {
        public string id { get; set; }
        public int type { get; set; }
        public bool permission { get; set; }
    }

    public class GuildApplicationCommandPermissions {
        public string id { get; set; }
        public string application_id { get; set; }
        public string guild_id { get; set; }
        public List<Permission> permissions { get; set; }
    }

    public class RoleMap {
        public ulong RoleID { get; set; }
        public List<(ulong GuildId, ulong RoleId)> Values { get; set; }
    }

}
