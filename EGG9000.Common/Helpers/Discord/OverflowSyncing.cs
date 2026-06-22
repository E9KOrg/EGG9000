using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database.Entities;
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

            var overflowCommands = (await Task.WhenAll(overflowServers.Select(x => x.GetApplicationCommandsAsync()))).SelectMany(x => x).ToList();

            foreach(var command in commands) {
                var permissions = await DiscordRest.GetAsBot<GuildApplicationCommandPermissions>($"applications/{KnownUsers.Bot}/guilds/{mainServer.Id}/commands/{command.Id}/permissions", client_secret);

                if(permissions.Permissions is null)
                    continue;
                foreach(var overflowServer in overflowServers) {
                    var overflowPermissions = new GuildApplicationCommandPermissions {
                        Permissions = []
                    };

                    foreach(var p in permissions.Permissions) {
                        var np = new Permission {
                            Id = p.Type == 1 && p.Id != mainServer.EveryoneRole.Id.ToString() ? roleMaps.First(y => y.RoleID.ToString() == p.Id).Values.First(y => y.GuildId == overflowServer.Id).RoleId.ToString() : p.Id,
                            PermissionBool = p.PermissionBool,
                            Type = p.Type
                        };
                        if(np.Type == 3)
                            continue;
                        overflowPermissions.Permissions.Add(np);
                    }

                    var overflowCommand = overflowCommands.FirstOrDefault(x => x.Guild.Id == overflowServer.Id && x.Name == command.Name);

                    var currentOverflowPermissions = await DiscordRest.GetAsBot<GuildApplicationCommandPermissions>($"applications/{KnownUsers.Bot}/guilds/{overflowServer.Id}/commands/{overflowCommand.Id}/permissions", client_secret);


                    var match = true;
                    if(currentOverflowPermissions.Permissions is not null && overflowPermissions.Permissions.Count == currentOverflowPermissions.Permissions.Count) {
                        foreach(var permission in overflowPermissions.Permissions) {
                            if(!currentOverflowPermissions.Permissions.Any(x => x.Id == permission.Id && x.PermissionBool == permission.PermissionBool && x.Type == permission.Type)) {
                                match = false;
                                break;
                            }
                        }
                    } else {
                        match = false;
                    }

                    if(match == false) {
                        var response = await DiscordRest.PutAsUser<GuildApplicationCommandPermissions, GuildApplicationCommandPermissions>($"applications/{KnownUsers.Bot}/guilds/{overflowServer.Id}/commands/{overflowCommand.Id}/permissions", user_access_token, overflowPermissions);
                        sb.AppendLine("Permissions for " + command.Name + " on " + overflowServer.Name);
                    } else {
                        sb.AppendLine("Skipping permissions for " + command.Name + " on " + overflowServer.Name);
                    }
                }
            }
            await mainServer.GetApplicationCommandsAsync();

            return sb.ToString();
        }

        public static List<RoleMap> GetRoleMaps(IList<SocketRole> rolesToSync, IEnumerable<SocketGuild> overflowServers) {
            var roleMaps = rolesToSync.Select(x => {
                var map = new RoleMap {
                    RoleID = x.Id,
                    Values = [],
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
        public string Id { get; set; }
        public int Type { get; set; }
        public bool PermissionBool { get; set; }
    }

    public class GuildApplicationCommandPermissions {
        public string Id { get; set; }
        public string ApplicationId { get; set; }
        public string GuildId { get; set; }
        public List<Permission> Permissions { get; set; }
    }

    public class RoleMap {
        public ulong RoleID { get; set; }
        public List<(ulong GuildId, ulong RoleId)> Values { get; set; }
    }

}
