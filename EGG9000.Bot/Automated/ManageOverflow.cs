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
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class ManageOverflow : _UpdaterBase {
        private IConfiguration _configuration;

        public ManageOverflow(IConfiguration Configuration,
            DiscordHostedService client,
            Bugsnag.IClient bugsnag,
            IConfiguration configuration
            ) : base(TimeSpan.FromMinutes(5.6), TimeSpan.FromMinutes(0), client, bugsnag, configuration) {
            _configuration = Configuration;
        }



        public override async Task Run(object state) {
            var _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
            var guilds = await _db.Guilds.AsQueryable().ToListAsync();

            foreach(var guild in guilds.Where(x => x.OverflowServers.Count > 0)) {
                Console.Write($"Manage Overflow for {guild.Name}");
                var mainServer = _client.Guilds.First(x => x.Id == guild.DiscordSeverId);
                var overflowServers = _client.Guilds.Where(x => guild.OverflowServers.Contains(x.Id));
                //var overflowServer = _client.Guilds.First(x => x.Id == 763854787912794183);
                await mainServer.DownloadUsersAsync();
                foreach(var server in overflowServers) {
                    await server.DownloadUsersAsync();
                }


                const ulong overflowRoleID = 775547850134257675;
                const ulong activeRoleID = 798284088967430144;

                var onlyMain = mainServer.Users.Where(x => !overflowServers.All(o => o.Users.Any(y => y.Id == x.Id)) && !x.IsBot);
                var both = mainServer.Users.Where(x => (overflowServers.All(o => o.Users.Any(y => y.Id == x.Id)) || !x.Roles.Any(y => y.Id == activeRoleID)) && !x.IsBot);

                var bothAllWithRole = both.Where(x => x.Roles.Any(y => y.Id == overflowRoleID));

                var onlyMainWithoutRole = onlyMain.Where(x => !x.Roles.Any(y => y.Id == overflowRoleID) && x.Roles.Count > 2 && x.Roles.Any(y => y.Id == activeRoleID));

                Console.Write($"Manage Overflow Roles for {guild.Name}");
                var role = mainServer.Roles.First(x => x.Id == overflowRoleID);
                foreach(var u in onlyMainWithoutRole) {
                    await u.AddRoleAsync(role);
                    Console.WriteLine($"Adding overflow role for {u.GetName()}");
                    await Task.Delay(750);
                }

                foreach(var u in mainServer.Users.Where(x => x.Roles.Count == 1 && x.Roles.Any(y => y.Id == overflowRoleID) && !x.IsBot)) {
                    await u.RemoveRoleAsync(role);
                    Console.WriteLine($"Removing overflow role for {u.GetName()}, it was the only role");
                    await Task.Delay(750);
                }

                foreach(var u in bothAllWithRole) {
                    await u.RemoveRoleAsync(role);
                    Console.WriteLine($"Removing overflow role for {u.GetName()}, they were in all servers.");
                    await Task.Delay(750);
                }

                
                foreach(var overflowServer in overflowServers) {
                    Console.Write($"Manage Nicknames for {guild.Name} in {overflowServer.Name}");
                    var onlyOverflow = overflowServer.Users.Where(x => !mainServer.Users.Any(y => y.Id == x.Id) && !x.IsBot);
                    foreach(var u in onlyOverflow) {
                        await u.KickAsync("No longer in main server");
                        Console.WriteLine($"Kicking {u.GetName()}");
                        await Task.Delay(750);
                    }

                    foreach(var overflowUser in overflowServer.Users) {
                        var mainServerUser = mainServer.Users.FirstOrDefault(x => x.Id == overflowUser.Id);
                        if(mainServerUser == null)
                            continue;
                        if(overflowUser.Nickname != mainServerUser.Nickname && !overflowUser.IsBot)
                        { // && !overflowUser.Roles.Any(x => x.Id == 764467748226334720)
                            try {
                                Console.WriteLine($"Changing nickname for {mainServerUser.Nickname}, it was {overflowUser.Nickname}. Server: {overflowServer.Name}");
                                await overflowUser.ModifyAsync(x => x.Nickname = mainServerUser.Nickname);

                                await Task.Delay(750);
                            } catch(Exception) {
                                Console.WriteLine($"Unable to change nickname for {mainServerUser.Nickname}");
                            }
                        }
                    }
                }
            }
        }
    }
}