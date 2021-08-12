//using EGG9000.Common.Database;
//using EGG9000.Common.Database.Entities;

//using Microsoft.Extensions.Caching.Memory;
//using Microsoft.Extensions.Hosting;

//using Newtonsoft.Json;

//using Polly;
//using Polly.Retry;

//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Net;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;

//using static EGG9000.Common.Helpers.Prefarm;

//namespace EGG9000.Bot.Services {
//    public class BackupRequest2 {
//        public string UserId { get; set; }
//        public double LastBackupTime { get; set; }
//    }

//    public class APILink2 {
//        private IMemoryCache _cache;
//        private HttpClient _httpClient;

//        private AsyncRetryPolicy sqlExcpetionPolicy = Policy.Handle<Exception>(ex => !(ex is OperationCanceledException)).WaitAndRetryAsync(new[]{
//                    TimeSpan.FromSeconds(1),
//                    TimeSpan.FromSeconds(2),
//                    TimeSpan.FromSeconds(3)
//                });


//        public APILink2(IMemoryCache memoryCache) {
//            _cache = memoryCache;
//            _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
//        }

//        private string GetUserBackupKey(string UserId) => $"UserBackup-{UserId}";

//        public void AddBackups(List<DBUser> users) {
//            foreach(var user in users.Where(x => x.LastBackup != null)) {
//                foreach(var backup in user.LastBackup) {
//                    if(backup?.Contracts != null && !string.IsNullOrEmpty(backup.EiUserId)) {
//                        var key = GetUserBackupKey(backup.EiUserId);
//                        _cache.Set(key, backup, DateTimeOffset.Now.AddDays(7));
//                        //Console.WriteLine($"Adding backup for {key} {backup.Settings.LastBackupTime}");
//                    }
//                }
//            }
//        }

//        public async Task<List<LeaderboardUser>> GetUserBackups(List<DBUser> users, ApplicationDbContext db, bool longBackup = false) {
//            var backupsNeeded = new List<BackupRequest>();
//            var lUsers = new List<LeaderboardUser>();

//            foreach(var user in users) {
//                foreach(var egginc in user.EggIncIds.Where(x => !string.IsNullOrWhiteSpace(x.Id))) {
//                    var key = GetUserBackupKey(egginc.Id);
//                    Ei.Backup currentBackup;
//                    double lastBackupTime = -1;
//                    if(_cache.TryGetValue(key, out currentBackup)) {
//                        if((DateTime.Now - currentBackup.CacheAdded).TotalMinutes < (longBackup ? 30 : 5)) {
//                            lUsers.Add(new LeaderboardUser {
//                                Backup = currentBackup,
//                                User = user
//                            });
//                            //Console.WriteLine("Local Cache");
//                            continue;
//                        }

//                        if(currentBackup.Settings?.LastBackupTime != null) {
//                            lastBackupTime = currentBackup.Settings.LastBackupTime;
//                        }
//                    } else {
//                        //Console.WriteLine($"Cache Miss {key}");
//                    }
//                    backupsNeeded.Add(new BackupRequest { UserId = egginc.Id, LastBackupTime = lastBackupTime });
//                }
//            }

//            if(backupsNeeded.Count > 0) {
//                foreach(var partition in Partition(backupsNeeded, 250)) {
//                    var url = "http://egg9000apilinksite.sglade.com/Home/GetBackups";
//                    //var url = "http://localhost:5014/Home/GetBackups";
//                    var response = await sqlExcpetionPolicy.ExecuteAsync(async () => await SendAsync<List<Ei.EggIncFirstContactResponse>>(url, partition, HttpMethod.Get));

//                    Console.WriteLine($"                                                   Changed {response.Data.Count(x => !x.Unchanged)}  Unchanged {response.Data.Count(x => x.Unchanged)}");
//                    foreach(var firstContactResponse in response.Data) {
//                        var key = GetUserBackupKey(firstContactResponse.EiUserId);
//                        var user = users.First(x => x.EggIncIds.Any(y => y.Id == firstContactResponse.EiUserId));
//                        if(firstContactResponse.Unchanged) {
//                            Ei.EggIncFirstContactResponse currentBackup;
//                            if(_cache.TryGetValue(key, out currentBackup)) {
//                                //Console.WriteLine("Unchanged");
//                                lUsers.Add(new LeaderboardUser {
//                                    Backup = currentBackup.Backup,
//                                    User = users.First(x => x.EggIncIds.Any(y => y.Id == firstContactResponse.EiUserId))
//                                });
//                                continue;
//                            }
//                        }
//                        lUsers.Add(new LeaderboardUser {
//                            Backup = firstContactResponse.Backup,
//                            User = user
//                        });
//                        if(firstContactResponse.Backup?.Contracts != null) {

//                            firstContactResponse.Backup.CacheAdded = DateTime.Now;
//                            //Console.WriteLine($"Changed {key} {firstContactResponse.Backup.Settings.LastBackupTime}");

//                            _cache.Set(key, firstContactResponse.Backup, DateTimeOffset.Now.AddDays(7));
//                            var userBackups = user.LastBackup?.ToList() ?? new List<Ei.Backup>();
//                            userBackups = userBackups.Where(x => x != null && x.EiUserId != firstContactResponse.EiUserId).ToList();
//                            userBackups.Add(firstContactResponse.Backup);
//                            user.LastBackup = userBackups;
//                        }
//                    }
//                    await db.SaveChangesAsync();
//                    Console.WriteLine($"                                                 Backup count {lUsers.Count}");

//                }
//            }
//            return lUsers;
//        }

//        public async Task<Ei.EggIncFirstContactResponse> GetBackup(string UserId) {
//            var key = GetUserBackupKey(UserId);
//            Ei.EggIncFirstContactResponse currentBackup;
//            double lastBackupTime = -123;
//            if(_cache.TryGetValue(key, out currentBackup)) {
//                if(currentBackup.Backup?.Settings?.LastBackupTime != null) {
//                    lastBackupTime = currentBackup.Backup.Settings.LastBackupTime;
//                }
//            }
//            string errorMessage;
//            try {
//                HttpResponseMessage response;
//                using(var request = new HttpRequestMessage(HttpMethod.Get, "http://egg9000apilinksite.sglade.com/Home/GetBackup")) {
//                    //Add content
//                    var content = JsonConvert.SerializeObject(new BackupRequest { LastBackupTime = lastBackupTime, UserId = UserId });
//                    request.Content = new StringContent(content, Encoding.UTF8, "application/json");
//                    //Add headers
//                    request.Headers.Accept.Clear();
//                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

//                    //Send the request
//                    response = await _httpClient.SendAsync(request);
//                    if(response.IsSuccessStatusCode) {
//                        var json = await response.Content.ReadAsStringAsync();
//                        var backupResponse = JsonConvert.DeserializeObject<Ei.EggIncFirstContactResponse>(json);
//                        if(backupResponse.Unchanged) {
//                            //Console.WriteLine($"Unchanged! {json.Length}");
//                            return currentBackup;
//                        }
//                        //Console.WriteLine($"Changed! {json.Length}");
//                        _cache.Set(key, backupResponse, DateTimeOffset.Now.AddDays(7));
//                        return backupResponse;
//                    } else {
//                        errorMessage = response.StatusCode.ToString();
//                    }
//                }
//            } catch(Exception e) {
//                errorMessage = e.Message;
//            }

//            if(currentBackup?.Backup != null) {
//                return currentBackup;
//            }

//            return new Ei.EggIncFirstContactResponse { Error = errorMessage, Success = false };
//        }

//        public class ApiResponse<T> {
//            public HttpStatusCode StatusCode { get; set; }
//            public string Message { get; set; }
//            public T Data { get; set; }
//        }

//        public static IEnumerable<List<T>> Partition<T>(IList<T> source, Int32 size) {
//            for(int i = 0; i < Math.Ceiling(source.Count / (Double)size); i++)
//                yield return new List<T>(source.Skip(size * i).Take(size));
//        }

//        public async Task<ApiResponse<TOut>> SendAsync<TOut>(string uri, object param, HttpMethod httpMethod) {
//            if(string.IsNullOrWhiteSpace(uri))
//                throw new Exception($"{nameof(uri)} can not be null or empty.");

//            var paramListForLog = JsonConvert.SerializeObject(param);


//            var url = new Uri(uri, UriKind.Absolute);

//            try {

//                HttpResponseMessage response;
//                using(var request = new HttpRequestMessage(httpMethod, url)) {
//                    //Add content
//                    if(param != null) {
//                        var content = JsonConvert.SerializeObject(param);
//                        request.Content = new StringContent(content, Encoding.UTF8, "application/json");
//                    }
//                    //Add headers
//                    request.Headers.Accept.Clear();
//                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

//                    //Send the request
//                    response = await _httpClient.SendAsync(request);
//                }

//                //If success
//                if(response.IsSuccessStatusCode) {
//                    var json = await response.Content.ReadAsStringAsync();
//                    var data = JsonConvert.DeserializeObject<TOut>(json);
//                    return new ApiResponse<TOut> {
//                        StatusCode = response.StatusCode,
//                        Data = data
//                    };
//                }

//                //If failure
//                var error = await response.Content.ReadAsStringAsync();
//                return new ApiResponse<TOut> {
//                    StatusCode = response.StatusCode,
//                    Message = error
//                };
//            }
//            //If unknown error
//            catch(Exception ex) {
//                var webEx = new Exception($"An error occured calling {httpMethod} for {url}. Error was: {ex.Message}", ex);
//                throw webEx;
//            }
//        }

//    }
//}
