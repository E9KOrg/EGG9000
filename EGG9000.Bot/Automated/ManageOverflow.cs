using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Bot.Commands;
using Discord.Rest;
using System.Numerics;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using Humanizer;
using Microsoft.Extensions.Caching.Memory;
using static EGG9000.Common.Helpers.Prefarm;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using EGG9000.Common.Migrations;
using EGG9000.Common.Helpers.Discord;

namespace EGG9000.Bot.Automated {
    public class ManageOverflow : _UpdaterBase<ManageOverflow> {

        public ManageOverflow(
            IServiceProvider provider
            ) : base(TimeSpan.FromMinutes(5.6), TimeSpan.FromMinutes(0), provider) {
        }



        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guilds = await _db.Guilds.AsQueryable().ToListAsync();

            var users = await _db.DBUsers.Select(x => new { x.Id, x.DiscordId, x.GuildId, x.LastGuild }).ToListAsync();
            foreach(var guild in guilds) {
                var mainServer = _client.Guilds.First(x => x.Id == guild.DiscordSeverId);
                await mainServer.DownloadUsersAsync();

                var members = users.Where(x => x.GuildId == guild.Id);
                var missingFromServer = members.Where(x => mainServer.GetUser(x.DiscordId) is null).Select(x => x.Id).ToList();

                var membersMissing = await _db.DBUsers.Where(x => missingFromServer.Contains(x.Id)).ToListAsync();
                membersMissing.ForEach(x => {
                    x.GuildId = 0; x.LastGuild = guild.Id;
                    _logger.LogInformation("Removing member from the guild {name}", x.DiscordUsername);
                });

                var returned = users.Where(x => x.GuildId == 0 && x.LastGuild == guild.Id && mainServer.GetUser(x.DiscordId) is not null).Select(x => x.Id).ToList();
                var membersReturn = await _db.DBUsers.Where(x => returned.Contains(x.Id)).ToListAsync();
                membersReturn.ForEach(x => {
                    x.GuildId = guild.Id;
                    _logger.LogInformation("Return member to the guild {name}", x.DiscordUsername);
                });

                await _db.SaveChangesAsync();
                StillAlive();
            }


            foreach(var guild in guilds.Where(x => x.OverflowServers.Count > 0)) {
                if(cancellationToken.IsCancellationRequested) {
                    break;
                }
                _logger.LogInformation("Manage Overflow for {guildName}", guild.Name);
                var mainServer = _client.Guilds.First(x => x.Id == guild.DiscordSeverId);
                var overflowServers = _client.Guilds.Where(x => guild.OverflowServers.Contains(x.Id));
                //var overflowServer = _client.Guilds.First(x => x.Id == 763854787912794183);
                await mainServer.DownloadUsersAsync();
                foreach(var server in overflowServers) {
                    await server.DownloadUsersAsync();
                }
                await HandleChannelPermissionSyncs(guild, mainServer, overflowServers, cancellationToken);
                await HandleRoleSyncs(guild, mainServer, overflowServers, cancellationToken);
                _client.RoleUpdated += _client_RoleUpdated;

#if DEBUG
                //continue;
#pragma warning disable CS0162 // Unreachable code detected
                _ = 1;
#pragma warning restore CS0162 // Unreachable code detected
#endif


                const ulong overflowRoleID = 775547850134257675;
                //const ulong activeRoleID = 798284088967430144;
                const ulong registeredRoleID = 794713762396897280;


                var onlyMain = mainServer.Users.Where(x => !overflowServers.All(o => o.Users.Any(y => y.Id == x.Id)) && !x.IsBot);
                var allOverflows = mainServer.Users.Where(x => (overflowServers.All(o => o.Users.Any(y => y.Id == x.Id)) || !x.Roles.Any(y => y.Id == registeredRoleID)) && !x.IsBot);

                var bothAllWithRole = allOverflows.Where(x => x.Roles.Any(y => y.Id == overflowRoleID));

                var onlyMainWithoutRole = onlyMain.Where(x => !x.Roles.Any(y => y.Id == overflowRoleID) && x.Roles.Count > 2 && x.Roles.Any(y => y.Id == registeredRoleID));

                var role = mainServer.Roles.FirstOrDefault(x => x.Id == overflowRoleID);
                if(role is null) {
                    _logger.LogWarning("Unable to find overflow role for {guild}", guild.Name);
                    continue;
                }


                foreach(var u in onlyMainWithoutRole) {
                    if(cancellationToken.IsCancellationRequested) {
                        break;
                    }
                    await u.AddRoleAsync(role);
                    _logger.LogInformation("Adding overflow role for {user}", u.GetName());
                }

                foreach(var u in mainServer.Users.Where(x => x.Roles.Count == 1 && x.Roles.Any(y => y.Id == overflowRoleID) && !x.IsBot)) {
                    if(cancellationToken.IsCancellationRequested) {
                        break;
                    }
                    await u.RemoveRoleAsync(role);
                    _logger.LogInformation("Removing overflow role for {user}, it was the only role", u.GetName());
                }

                foreach(var u in bothAllWithRole) {
                    if(cancellationToken.IsCancellationRequested) {
                        break;
                    }
                    await u.RemoveRoleAsync(role);
                    _logger.LogInformation("Removing overflow role for {user}, they were in all servers.", u.GetName());
                }


                foreach(var overflowServer in overflowServers) {
                    if(cancellationToken.IsCancellationRequested) {
                        break;
                    }
                    var onlyOverflow = overflowServer.Users.Where(x => !mainServer.Users.Any(y => y.Id == x.Id) && !x.IsBot);
                    foreach(var u in onlyOverflow) {
                        await u.KickAsync("No longer in main server");
                        _logger.LogInformation("Kicking {user}, no longer in main server", u.GetName());
                    }

                    foreach(var overflowUser in overflowServer.Users) {
                        if(cancellationToken.IsCancellationRequested) {
                            break;
                        }
                        var mainServerUser = mainServer.Users.FirstOrDefault(x => x.Id == overflowUser.Id);
                        if(mainServerUser == null)
                            continue;
                        if(overflowUser.Nickname != mainServerUser.Nickname && !overflowUser.IsBot && overflowUser.Guild.OwnerId != overflowUser.Id) { // && !overflowUser.Roles.Any(x => x.Id == 764467748226334720)
                            try {
                                _logger.LogInformation("Changing nickname for {newName}, it was {currentName} in {overflow}", mainServerUser.Nickname, overflowUser.Nickname, overflowServer.Name);
                                await overflowUser.ModifyAsync(x => x.Nickname = mainServerUser.Nickname);
                            } catch(Exception) {
                                _logger.LogWarning("Unable to change nickname for {user}", mainServerUser.GetName());
                            }
                        }
                    }
                }
                StillAlive();
            }
        }

        private Task _client_RoleUpdated(SocketRole originalRole, SocketRole updatedRole) {
            _ = Task.Run(async () => {
                await UpdateRoles(originalRole, updatedRole);
            });

            return Task.CompletedTask;
        }

        private async Task UpdateRoles(SocketRole originalRole, SocketRole updatedRole) { 
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guild = await _db.Guilds.FirstOrDefaultAsync(x => x.Id == originalRole.Guild.Id);
            if(!guild.OverflowServers.Any() || guild.RolesToSync is null)
                return;

            if(!guild.RolesToSync.Contains(originalRole.Id.ToString()))
                return;

            var overflowServers = _client.Guilds.Where(x => guild.OverflowServers.Contains(x.Id));
            foreach(var overflowServer in overflowServers) {
                var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == originalRole.Name);
                if(overflowRole != null) {
                    //await overflowRole.ModifyAsync(async x => {
                    await overflowRole.ModifyAsync(x => {
                        x.Name = updatedRole.Name;
                        x.Color = updatedRole.Color;
                        x.Permissions = updatedRole.Permissions;
                        //if(updatedRole.Icon != originalRole.Icon) {
                        //    x.Icon = new Image(await DownloadImage(updatedRole.GetIconUrl()));
                        //}
                    });
                }
            }
        }







        private async Task HandleRoleSyncs(Guild guild, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            if(guild.RolesToSync is null)
                return;
            var roleids = guild.RolesToSync.Split(",");
            var rolesToSync = mainServer.Roles.Where(x => roleids.Any(y => y == x.Id.ToString()));

            var roleMaps = OverflowSyncing.GetRoleMaps(rolesToSync.ToList(), overflowServers);


            foreach(var overflowServer in overflowServers) {
                StillAlive();
                if(cancellationToken.IsCancellationRequested) break;

                //Add missing roles
                foreach(var role in rolesToSync.OrderByDescending(x => x.Position)) {
                    if(cancellationToken.IsCancellationRequested) break;

                    IRole overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                    if(overflowRole is null) {
                        overflowRole = await overflowServer.CreateRoleAsync(role.Name, color: role.Color);
                        //if(!string.IsNullOrEmpty(role.Icon)) {
                        //    await newRole.ModifyAsync(async x => x.Icon = new Image(await DownloadImage(role.GetIconUrl())));
                        //}
                    }/* else if(overflowRole.Icon is null && role.Icon is not null) {
                        //var image = new Image(await DownloadImage(role.GetIconUrl()));
                        //await overflowRole.ModifyAsync(x => x.Icon = image);
                    }*/
                    else if(!role.Permissions.Equals(overflowRole.Permissions)) {
                        await overflowRole.ModifyAsync(x => {
                            x.Name = role.Name;
                            x.Color = role.Color;
                            x.Permissions = role.Permissions;
                        });
                    }
                }

                //Sync user roles
                for(var i = 0; i < overflowServer.Users.Count; i++) {
                    StillAlive();
                    var overflowUser = overflowServer.Users.ElementAt(i);
                    //foreach(var overflowUser in overflowServer.Users) {
                    if(cancellationToken.IsCancellationRequested) {
                        break;
                    }
                    var mainServerUser = mainServer.Users.FirstOrDefault(x => x.Id == overflowUser.Id);
                    if(mainServerUser == null)
                        continue;

                    var neededRoles = new List<SocketRole>();
                    var removeRoles = new List<SocketRole>();
                    foreach(var role in rolesToSync) {
                        StillAlive();
                        var hasRoleInMain = mainServerUser.Roles.Any(x => x.Name == role.Name);
                        var hasRoleInOverflow = overflowUser.Roles.Any(x => x.Name == role.Name);
                        var overflowRole = overflowServer.Roles.FirstOrDefault(x => x.Name == role.Name);
                        if(hasRoleInMain && !hasRoleInOverflow && overflowRole is not null) {
                            neededRoles.Add(overflowRole);
                        }
                        if(!hasRoleInMain && hasRoleInOverflow && overflowRole is not null) {
                            removeRoles.Add(overflowRole);
                        }
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

            //Update role maps
            //await HandleCommandPermissionSyncs(guild, mainServer, overflowServers, cancellationToken, roleMaps);
        }

        private async Task<MemoryStream> DownloadImage(string url) {
            using(var httpClient = new HttpClient()) {
                var imageContent = await httpClient.GetByteArrayAsync(url);

                var imageBuffer = new MemoryStream(imageContent);
                imageBuffer.Position = 0;
                return imageBuffer;
            }
        }
        private async Task HandleChannelPermissionSyncs(Guild guild, SocketGuild mainServer, IEnumerable<SocketGuild> overflowServers, CancellationToken cancellationToken) {
            var mainCoopCategory = (await _client.GetAllCoopCategories(mainServer)).First();
            var roles = mainServer.Roles.Where(x => mainCoopCategory.PermissionOverwrites.Any(y => y.TargetType == PermissionTarget.Role && y.TargetId == x.Id)).ToList();

            foreach(var overflowServer in overflowServers) {
                var matches = mainCoopCategory.PermissionOverwrites.Where(y => y.TargetType == PermissionTarget.Role).Select(x => {
                    var mainRole = mainServer.Roles.First(r => r.Id == x.TargetId);
                    return new OverflowPermissionRoleMatch {
                        MainRole = mainRole,
                        OverflowRole = overflowServer.Roles.First(x => x.Name == mainRole.Name),
                        Overwrite = x
                    };
                });
                var coopCategories = await _client.GetAllCoopCategories(overflowServer);
                foreach(var coopCategory in coopCategories) {
                    foreach(var overwrite in coopCategory.PermissionOverwrites) {
                        StillAlive();
                        var match = matches.FirstOrDefault(x => x.OverflowRole.Id == overwrite.TargetId);
                        if(match == null) {
                            if(overwrite.TargetType == PermissionTarget.Role) {
                                await coopCategory.RemovePermissionOverwriteAsync(overflowServer.GetRole(overwrite.TargetId));
                            } else {
                                await coopCategory.RemovePermissionOverwriteAsync(overflowServer.GetUser(overwrite.TargetId));
                            }

                        } else {
                            if(!Compare(overwrite.Permissions, match.Overwrite.Permissions)) {
                                await coopCategory.AddPermissionOverwriteAsync(match.OverflowRole, match.Overwrite.Permissions);
                            }
                        }
                    }

                    foreach(var match in matches.Where(x => !coopCategory.PermissionOverwrites.Any(y => y.TargetId == x.OverflowRole.Id))) {
                        StillAlive();
                        await coopCategory.AddPermissionOverwriteAsync(match.OverflowRole, match.Overwrite.Permissions);
                    }

                    //Temp to fix missing role permissions
                    //foreach(var channel in coopCategory.Channels) {
                    //    foreach(var match in matches.Where(x => !x.OverflowRole.IsEveryone)) {
                    //        await channel.AddPermissionOverwriteAsync(match.OverflowRole, match.Overwrite.Permissions);
                    //    }
                    //}
                }
            }
        }
        public bool Compare(OverwritePermissions x, OverwritePermissions y) {
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