using Discord;
using Discord.WebSocket;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Services;
using EGG9000.Common.SharedModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.APILinkSite.Controllers {
    public class HomeController : Controller {
        private readonly ILogger<HomeController> _logger;
        private IMemoryCache _cache;
        private DiscordBasicService _discord;
        //private Bugsnag.IClient _bugsnag;

        public HomeController(ILogger<HomeController> logger, IMemoryCache memoryCache, DiscordBasicService discord) {
            _logger = logger;
            _cache = memoryCache;
            _discord = discord;
            //_bugsnag = bugsnag;
        }
        public IActionResult Ping() {
            return Content("Pong");
        }
        public IActionResult Index() {
            return NotFound();
        }

        public async Task<IActionResult> GetBackup([FromBody] BackupRequest request) {
            return Json(await _getBackup(request));
        }

        public async Task<IActionResult> GetBackups([FromBody] List<BackupRequest> requests) {
            var queue = new ConcurrentQueue<BackupResponse>();
            await ForEachAsync(requests, 10, async (request) => {
                queue.Enqueue(await _getBackup(request));
            });

            return Json(queue.ToList());
        }

        private async Task<BackupResponse> _getBackup(BackupRequest request) {
            Ei.EggIncFirstContactResponse backup;
            //var hasCache = _cache.TryGetValue(request.UserId, out backup);
            //if(hasCache && backup.Backup?.Settings?.LastBackupTime == request.LastBackupTime) {
            //    //_logger.LogInformation($"Unchanged: ID {request.UserId} LastBackTime {request.LastBackupTime}");
            //    return new Ei.EggIncFirstContactResponse { Unchanged = true, EiUserId = request.UserId };
            //}

            backup = await ContractsAPI.FirstContact(request.UserId);
            
            if(backup.Backup?.Settings != null && (float)backup.Backup.Settings.LastBackupTime == request.LastBackupTime) {
                return new BackupResponse { Unchanged = true, EggIncId = request.UserId };
            }
            backup.EiUserId = request.UserId;

            //_cache.Set(request.UserId, backup, DateTimeOffset.Now.AddDays(7));
            //_logger.LogInformation($"Changed: ID {request.UserId} LastBackTime {request.LastBackupTime} NewLastBackupTime {backup.Backup?.Settings?.LastBackupTime}");
            //_bugsnag.Breadcrumbs.Leave($"Attempting to get custombackup for {request.UserId}");

            try {
                var customBackup = new CustomBackup(backup.Backup);
                return new BackupResponse {
                    Backup = customBackup,
                    EggIncId = request.UserId, Unchanged = false
                };
            } catch (Exception e) {
                _logger.LogError(e, $"Attempted to get custombackup for {request.UserId}");
                return new BackupResponse {
                    EggIncId = request.UserId, Unchanged = true
                };
            }
        }

        public static Task ForEachAsync<T>(IEnumerable<T> source, int dop, Func<T, Task> body) {
            return Task.WhenAll(
                from partition in Partitioner.Create(source).GetPartitions(dop)
                select Task.Run(async delegate {
                    using(partition)
                        while(partition.MoveNext())
                            await body(partition.Current);
                }));
        }

        public async Task<IActionResult> AddUsersToChannel([FromBody] CoopPermissions coopPermissions) {
            var guild = _discord.Guilds.FirstOrDefault(x => x.Id == coopPermissions.GuildId);
            var coopChannel = guild.GetChannel(coopPermissions.ChannelId) as SocketTextChannel;
            List<ulong> addedUsers = new();
            foreach(var userid in coopPermissions.UserIds) {
                var user = guild.GetUser(userid);
                if(user is null) {
                    _logger.LogInformation($"Unable to find user {userid}");
                } else {
                    _logger.LogInformation($"Attempting to add user {user?.DisplayName}");
                    try {
                        await coopChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                        addedUsers.Add(userid);
                        _logger.LogInformation("Added user to channel {user}", user.DisplayName);
                    } catch(Exception e) {
                        _logger.LogWarning("Unable able to add {user} to {coop} in {server} ({error})", user.DisplayName, coopChannel.Name, guild.Name, e.Message);
                    }
                }
            }
            return Json(addedUsers);

        }
    }
}
