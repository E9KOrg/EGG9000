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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using EGG9000.Bot.EggIncAPI;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;

namespace EGG9000.Common.Services {
    public class APILinkOptions {
        public bool ReportUpdatedClientVersion = false;
        public bool AsyncLoadCache = false;
    }
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
//        //private static string urlBase = "http://localhost:5014/Home/";

//#if DEBUG
//        //private static string urlBase = "http://localhost:5014/Home/";
//        //private static string urlBase = "https://localhost:44316/Home/";
//        private static string urlBase = "http://egg9000apilinksite.sglade.com/Home/";
//#else
//        private static string urlBase = "http://egg9000apilinksite.sglade.com/Home/";
//#endif




        private IMemoryCache _cache;
        private HttpClient _httpClient;
        public IConfiguration _configuration;
        public IServiceProvider _provider;
        private bool _ReportUpdatedClientVersion;
        private int _LastClientVersion;
        private DiscordSocketClient _discord;
        private ILogger<APILink> _logger;
        private APILinkOptions _settings;

#if DEBUG
        private string urlBase => _configuration.GetConnectionString("APILinkURL");
        //private string urlBase => "http://localhost:5014/Home/";
#else
        private string urlBase => _configuration.GetConnectionString("APILinkURL");
#endif

        public APILink(IConfiguration configuration, IServiceProvider provider, DiscordSocketClient discord, ILogger<APILink> logger) {
            _cache = new MemoryCache(new MemoryCacheOptions { });
            _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _configuration = configuration;
            _provider = provider;
            var options = provider.GetService<IOptionsMonitor<APILinkOptions>>();
            _settings = options.CurrentValue;
            _ReportUpdatedClientVersion = options.CurrentValue.ReportUpdatedClientVersion;
            _discord = discord;
            _logger = logger;
        }

        private string GetUserBackupKey(string UserId) => $"UserBackup-{UserId}";

        public void AddExistingBackups(IEnumerable<EggIncAccount> accounts) {
            foreach(var account in accounts.Where(x => x.Backup is not null)) {
                var key = GetUserBackupKey(account.Id);
                _cache.Set(key, account.Backup, DateTimeOffset.Now.AddDays(7));
            }
        }

        public async Task<List<LeaderboardUser>> GetUserBackups(List<DBUser> users, ApplicationDbContext db, CancellationToken token, bool longBackup = false, bool forceAll = false) {
            var eggIncIds = users.SelectMany(u => u.EggIncAccounts.Where(e => !string.IsNullOrWhiteSpace(e.Id)).Select(e => e.Id));
            var backups = await GetUserBackups(eggIncIds, token, longBackup, forceAll);
            var lUsers = new List<LeaderboardUser>();

            foreach(var user in users) {
                if(token.IsCancellationRequested)
                    return null;
                foreach(var eggInc in user.EggIncAccounts.Where(e => !string.IsNullOrEmpty(e.Id))) {
                    var backup = backups.FirstOrDefault(b => b.EggIncId == eggInc.Id);
                    var account = user.EggIncAccounts.FirstOrDefault(b => b.Id == eggInc.Id);

                    if(backup?.Farms != null && account is not null && (backup.LastBackupTime != account.Backup?.LastBackupTime || forceAll)) {
                        account.Backup = backup;
                        user.UpdateAccounts();
                    }

                    if(backup == null) {
                        backup = account?.Backup;
                    }

                    if(backup != null) {
                        lUsers.Add(new LeaderboardUser { User = user, Backup = backup });
                    } else {
                        _logger.LogWarning("Missing backup for {user} {eiid}", user.DiscordUsername, eggInc.Id);
                    }
                }
            }
            _logger.LogInformation("Saving {changecount} changes to db", db.ChangeTracker.Entries().Where(x => x.State != EntityState.Unchanged).Count());
            await db.SaveChangesAsync();
            return lUsers;
        }

        public async Task<List<CustomBackup>> GetUserBackups(IEnumerable<string> eggIncIds, CancellationToken token, bool longBackup = false, bool forceAll = false) {
            var backupsNeeded = new List<BackupRequest>();
            var backups = new List<CustomBackup>();

            foreach(var eggIncId in eggIncIds) {
                if(token.IsCancellationRequested)
                    return null;

                var key = GetUserBackupKey(eggIncId);
                CustomBackup currentBackup;
                float lastBackupTime = -1;
                if(!forceAll && _cache.TryGetValue(key, out currentBackup)) {
                    if(currentBackup.Farms is not null && !currentBackup.Farms.All(f => f.Vehicles == null)) {

                        if(currentBackup.CacheAdded < DateTime.Now.AddMinutes(10) && ((DateTime.Now - currentBackup.CacheAdded).TotalMinutes < 5 || longBackup)) {
                            backups.Add(currentBackup);
                            continue;
                        }

                        lastBackupTime = currentBackup.LastBackupTime;
                    }
                }
                if(eggIncId.StartsWith("EI")) {
                    backupsNeeded.Add(new BackupRequest { UserId = eggIncId, LastBackupTime = forceAll ? 0 : lastBackupTime });
                }
            }

            _logger.LogInformation("Backups from cache {count}", backups.Count);

            if(backupsNeeded.Count > 0) {
                var throttler = new SemaphoreSlim(2);
                var tasks = new List<Task>();
                var responses = new ConcurrentQueue<ApiResponse<List<Ei.EggIncFirstContactResponse>>>();
                var url = $"{urlBase}GetBackups";
                var partitions = Partition(backupsNeeded, 250);
                var i = 1;
                foreach(var partition in partitions) {
                    if(token.IsCancellationRequested)
                        return null;

                    await throttler.WaitAsync();
                    _logger.LogInformation("Handling partition {count} of {total}", i, partitions.Count());
                    i++;
                    tasks.Add(Task.Run(async () => {
                        try {
                            var response = await SendAsync<List<BackupResponse>>(url, partition, HttpMethod.Get);
                            if(response.Data is null) {
                                  _logger.LogError("Error getting backups for partition, status code: {code}", response.StatusCode);
                                return;
                            }
                            _logger.LogInformation("Changed {count} of {total}", response.Data.Count(x => !x.Unchanged), response.Data.Count);
                            foreach(var backupResponse in response.Data) {
                                var key = GetUserBackupKey(backupResponse.EggIncId);
                                if(backupResponse.Unchanged) {
                                    if(_cache.TryGetValue(key, out CustomBackup currentBackup)) {
                                        backups.Add(currentBackup);
                                        continue;
                                    }
                                }
                                if(!backupResponse.Backup.EmptyBackup) {
                                    if(_ReportUpdatedClientVersion && 
                                        backupResponse.Backup.ClientVersion > ContractsAPI.ClientVersion && 
                                        backupResponse.Backup.ClientVersion > _LastClientVersion) {
                                        _LastClientVersion = backupResponse.Backup.ClientVersion;
                                        _logger.LogWarning("ClietVersion Update from {CurrentVersion} {NewVesrion}", ContractsAPI.ClientVersion, _LastClientVersion);
                                        var kendromedmchannel = await _discord.GetUser(248865520756064257).CreateDMChannelAsync();
                                        if(kendromedmchannel is not null) {
                                            await kendromedmchannel.SendMessageAsync($"ClientVersion Update from {ContractsAPI.ClientVersion} to {_LastClientVersion}");
                                            ContractsAPI.ClientVersion = (uint)_LastClientVersion;
                                        } else {
                                            _logger.LogError("Unable to get DM channel for Kendrome");
                                        }
                                    }

                                    backups.Add(backupResponse.Backup);
                                    backupResponse.Backup.CacheAdded = DateTime.Now;
                                    _cache.Set(key, backupResponse.Backup, DateTimeOffset.Now.AddDays(7));
                                }
                            }
                        } catch(Exception e) {
                            _logger.LogError("Error getting backup from APILink {exception}", e);
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
                            return currentBackup;
                        }
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
            if(_settings.AsyncLoadCache) {
                _logger.LogInformation("Async Loading Users");
                _ = GetUsers();
            } else {
                await GetUsers();
            }
        }

        public async Task GetUsers() {
            _logger.LogInformation("Getting User Backups for Cache");
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var usersTask = await _db.DBUsers.AsQueryable().Where(x => x.GuildId > 0).ToListAsync();
            var backups = usersTask.SelectMany(x => x.EggIncAccounts);
            if(backups != null) {
                AddExistingBackups(backups);
            }
            _logger.LogInformation("Finished Getting User Backups for Cache");

        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }
    }
}
