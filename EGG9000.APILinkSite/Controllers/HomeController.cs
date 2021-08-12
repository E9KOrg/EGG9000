using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EGG9000.APILinkSite.Models;
using Microsoft.Extensions.Caching.Memory;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Services;
using System.Collections.Concurrent;
using EGG9000.Common.Database;

namespace EGG9000.APILinkSite.Controllers {
    public class HomeController : Controller {
        private readonly ILogger<HomeController> _logger;
        private IMemoryCache _cache;

        public HomeController(ILogger<HomeController> logger, IMemoryCache memoryCache) {
            _logger = logger;
            _cache = memoryCache;
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
            var customBackup = new CustomBackup(backup.Backup);
            return new BackupResponse {
                Backup = customBackup,
                EggIncId = request.UserId, Unchanged = false
            };
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
    }
}
