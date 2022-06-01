using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;

using Newtonsoft.Json;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Prefarm;
using Microsoft.EntityFrameworkCore;

namespace EGG9000.Bot.Services {
    public class BackupRequest {
        public string UserId { get; set; }
        public float LastBackupTime { get; set; }
    }

    public class BackupResponse {
        public bool Unchanged { get; set; }
        public string EggIncId { get; set; }
        public CustomBackup Backup { get; set; }
    }

    public class APILink : IHostedService {
        //private static string urlBase = "http://localhost:5014/Home/";
        //private static string urlBase = "https://localhost:44316/Home/";
        private static string urlBase = "http://egg9000apilinksite.sglade.com/Home/";

        private IMemoryCache _cache;
        private HttpClient _httpClient;
        private ApplicationDbContext _db;

        public APILink(ApplicationDbContext db) {
            _cache = new MemoryCache(new MemoryCacheOptions { });
            //_cache = memoryCache;
            _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            _db = db;
        }

        private string GetUserBackupKey(string UserId) => $"UserBackup-{UserId}";

        public void AddExistingBackups(IEnumerable<CustomBackup> backups) {
            foreach(var backup in backups) {
                var key = GetUserBackupKey(backup.EggIncId);
                _cache.Set(key, backup, DateTimeOffset.Now.AddDays(7));
            }
        }

        public async Task<List<LeaderboardUser>> GetUserBackups(List<DBUser> users, ApplicationDbContext db, bool longBackup = false) {
            var eggIncIds = users.SelectMany(u => u.EggIncIds.Where(e => !string.IsNullOrWhiteSpace(e.Id)).Select(e => e.Id));
            var backups = await GetUserBackups(eggIncIds, longBackup);

            var lUsers = new List<LeaderboardUser>();

            foreach(var user in users) {
                foreach(var eggInc in user.EggIncIds.Where(e => !string.IsNullOrEmpty(e.Id))) {
                    var backup = backups.FirstOrDefault(b => b.EggIncId == eggInc.Id);
                    var dbBackup = user.Backups?.FirstOrDefault(b => b.EggIncId == eggInc.Id);

                    if(backup != null && backup.LastBackupTime !=  dbBackup?.LastBackupTime) {
                        var userBackups = user.Backups?.ToList() ?? new List<CustomBackup>();
                        userBackups = userBackups.Where(x => x != null && x.EggIncId != eggInc.Id).ToList();
                        userBackups.Add(backup);
                        user.Backups = userBackups;
                    }

                    if(backup == null) {
                        backup = dbBackup;
                    }

                    if(backup != null) {
                        lUsers.Add(new LeaderboardUser { User = user, Backup = backup });
                    } else {
                        Console.WriteLine($"Missing backup for {user.DiscordUsername} {eggInc.Id}");
                    }
                }
            }

            await db.SaveChangesAsync();
            return lUsers;
        }

        public async Task<List<CustomBackup>> GetUserBackups(IEnumerable<string> eggIncIds, bool longBackup = false) {
            var backupsNeeded = new List<BackupRequest>();
            var backups = new List<CustomBackup>();

            foreach(var eggIncId in eggIncIds) {
                var key = GetUserBackupKey(eggIncId);
                CustomBackup currentBackup;
                float lastBackupTime = -1;
                if(_cache.TryGetValue(key, out currentBackup)) {
                    if(!currentBackup.Farms.All(f => f.Vehicles == null)) {

                        if(currentBackup.CacheAdded < DateTime.Now.AddMinutes(10) && ((DateTime.Now - currentBackup.CacheAdded).TotalMinutes < 5 || longBackup)) {
                            backups.Add(currentBackup);
                            continue;
                        }

                        lastBackupTime = currentBackup.LastBackupTime;
                    }
                }
                if(eggIncId.StartsWith("EI")) {
                    backupsNeeded.Add(new BackupRequest { UserId = eggIncId, LastBackupTime = lastBackupTime });
                }
            }

            Console.WriteLine($"Backups from cache {backups.Count}");

            if(backupsNeeded.Count > 0) {
                var throttler = new SemaphoreSlim(3);
                var tasks = new List<Task>();
                var responses = new ConcurrentQueue<ApiResponse<List<Ei.EggIncFirstContactResponse>>>();
                var url = $"{urlBase}GetBackups";
                var partitions = Partition(backupsNeeded, 500);
                var i = 1;
                foreach(var partition in partitions) {
                    await throttler.WaitAsync();
                    Console.WriteLine($"Handling partition {i++} of {partitions.Count()}");
                    tasks.Add(Task.Run(async () => {
                        try {
                            var response = await SendAsync<List<BackupResponse>>(url, partition, HttpMethod.Get);
                            Console.WriteLine($"                                                   Changed {response.Data.Count(x => !x.Unchanged)}  Unchanged {response.Data.Count(x => x.Unchanged)}");
                            foreach(var backupResponse in response.Data) {
                                var key = GetUserBackupKey(backupResponse.EggIncId);
                                if(backupResponse.Unchanged) {
                                    CustomBackup currentBackup;
                                    if(_cache.TryGetValue(key, out currentBackup)) {
                                        //Console.WriteLine("Unchanged");
                                        backups.Add(currentBackup);
                                        continue;
                                    }
                                }
                                if(!backupResponse.Backup.EmptyBackup) {
                                    backups.Add(backupResponse.Backup);
                                    backupResponse.Backup.CacheAdded = DateTime.Now;
                                    _cache.Set(key, backupResponse.Backup, DateTimeOffset.Now.AddDays(7));
                                } 
                            }
                        } catch(Exception) {
                            Console.WriteLine("Error getting backup from APILink");
                        } finally {
                            throttler.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);


                //foreach(var partition in Partition(backupsNeeded, 100)) {
                //var response = await SendAsync<List<Ei.EggIncFirstContactResponse>>(url, partition, HttpMethod.Get);
                foreach(var response in responses) {
                    //var response = task.Result;


                }
            }
            return backups;
        }

        public async Task<CustomBackup> GetBackup(string UserId) {
            var key = GetUserBackupKey(UserId);
            CustomBackup currentBackup;
            float lastBackupTime = -123;
            if(_cache.TryGetValue(key, out currentBackup)) {
                if(currentBackup.Farms != null && !currentBackup.Farms.All(f => f.Vehicles == null)) {
                    lastBackupTime = currentBackup.LastBackupTime;
                }
            }
            string errorMessage;
            try {
                HttpResponseMessage response;
                var url = $"{urlBase}GetBackup";
                using(var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                    //Add content
                    var content = JsonConvert.SerializeObject(new BackupRequest { LastBackupTime = lastBackupTime, UserId = UserId });
                    request.Content = new StringContent(content, Encoding.UTF8, "application/json");
                    //Add headers
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    //Send the request
                    response = await _httpClient.SendAsync(request);
                    if(response.IsSuccessStatusCode) {
                        var json = await response.Content.ReadAsStringAsync();
                        var backupResponse = JsonConvert.DeserializeObject<BackupResponse>(json);
                        if(backupResponse.Unchanged) {
                            //Console.WriteLine($"Unchanged! {json.Length}");
                            return currentBackup;
                        }
                        //Console.WriteLine($"Changed! {json.Length}");
                        if(backupResponse.Backup.Farms != null) {
                            _cache.Set(key, backupResponse.Backup, DateTimeOffset.Now.AddDays(7));
                        }
                        return backupResponse.Backup;
                    } else {
                        var errorContent = response.Content.ReadAsStringAsync();
                        errorMessage = response.StatusCode.ToString();
                    }
                }
            } catch(Exception e) {
                errorMessage = e.Message;
            }

            if(currentBackup != null) {
                return currentBackup;
            }

            return null;
        }

        public class ApiResponse<T> {
            public HttpStatusCode StatusCode { get; set; }
            public string Message { get; set; }
            public T Data { get; set; }
        }

        public static IEnumerable<List<T>> Partition<T>(IList<T> source, Int32 size) {
            for(int i = 0; i < Math.Ceiling(source.Count / (Double)size); i++)
                yield return new List<T>(source.Skip(size * i).Take(size));
        }

        public async Task<ApiResponse<TOut>> SendAsync<TOut>(string uri, object param, HttpMethod httpMethod) {
            if(string.IsNullOrWhiteSpace(uri))
                throw new Exception($"{nameof(uri)} can not be null or empty.");

            var paramListForLog = JsonConvert.SerializeObject(param);


            var url = new Uri(uri, UriKind.Absolute);

            try {

                HttpResponseMessage response;
                using(var request = new HttpRequestMessage(httpMethod, url)) {
                    //Add content
                    if(param != null) {
                        var content = JsonConvert.SerializeObject(param);
                        request.Content = new StringContent(content, Encoding.UTF8, "application/json");
                    }
                    //Add headers
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    //Send the request
                    response = await _httpClient.SendAsync(request);
                }

                //If success
                if(response.IsSuccessStatusCode) {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JsonConvert.DeserializeObject<TOut>(json);
                    return new ApiResponse<TOut> {
                        StatusCode = response.StatusCode,
                        Data = data
                    };
                }

                //If failure
                var error = await response.Content.ReadAsStringAsync();
                return new ApiResponse<TOut> {
                    StatusCode = response.StatusCode,
                    Message = error
                };
            }
            //If unknown error
            catch(Exception ex) {
                var webEx = new Exception($"An error occured calling {httpMethod} for {url}. Error was: {ex.Message}", ex);
                throw webEx;
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken) {
            Console.WriteLine("Getting User Backups for Cache");
            var usersTask = await _db.DBUsers.AsQueryable().Where(x => x.GuildId > 0).ToListAsync();
            var backups = usersTask.SelectMany(x => x.Backups ?? new List<CustomBackup>());
            if(backups != null) {
                AddExistingBackups(backups);
            }
            Console.WriteLine("Finished Getting User Backups for Cache");
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }
    }
}
