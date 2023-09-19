using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using EGG9000.Site.Models;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using EGG9000.Bot.EggIncAPI;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using EGG9000.Common.Database.Entities;
using Microsoft.AspNetCore.Cors;
using System.Net.Http;
using System.IO;
using Google.Protobuf;
using Discord.WebSocket;
using System.Text;
using Discord;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using EGG9000.Common.Helpers;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Services;
using Polly;
using static EGG9000.Common.Helpers.Prefarm;
using System.Text.Json;
using System.Text.Json.Serialization;
using Discord.Net;
using System.Text.RegularExpressions;
using Discord.Rest;
using Microsoft.Extensions.Caching.Memory;
using EGG9000.Bot;
using Stripe;
using System.Threading;
using System.Security.Claims;
using System.Security.Principal;
using EGG9000.Common.Contracts;
using EGG9000.Common.Factories;
using Microsoft.Data.SqlClient;

namespace EGG9000.Site.Controllers {
    public class HomeController : Controller {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly DiscordSocketClient _discord;
        private readonly APILink _apiLink;
        private readonly IMemoryCache _cache;
        private readonly SignInManager<IdentityUser> _signInManager;

        public HomeController(
            ILogger<HomeController> logger,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            SignInManager<IdentityUser> signInManager,
            DiscordSocketClient discord,
            APILink apiLink,
            ApplicationDbContext db, IMemoryCache cache) {
            _discord = discord;
            _roleManager = roleManager;
            _userManager = userManager;
            _logger = logger;
            _apiLink = apiLink;
            _db = db;
            _cache = cache;
            _signInManager = signInManager;
        }

#if DEBUG || DEV9002
        public async Task<IActionResult> DebugLogin([FromQuery] string id) {
            var a = await _db.UserLogins.FirstOrDefaultAsync(x => x.ProviderKey == id);
            var user = await _userManager.Users.FirstAsync(x => x.Id == a.UserId);
            var dbuser = await _db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == ulong.Parse(id));
            if(dbuser.GuildId != 1108127105088241746) {
                return NotFound();
            }
            await _signInManager.SignInWithClaimsAsync(user, true, new List<Claim> {
                new Claim("DbUserId", dbuser.Id.ToString()),
                new Claim("DiscordId", id),
                new Claim("GuildId", dbuser.GuildId.ToString())
            });
            return Redirect("/");
        }
#endif

        public async Task<IActionResult> Test() {
            var demerits = await _db.Demerit.Where(x => x.When > DateTimeOffset.Now.AddHours(-10)).ToListAsync();
            _db.RemoveRange(demerits);
            await _db.SaveChangesAsync();
            var coops = await _db.Coops.Where(x => !x.DeletedChannel).ToListAsync();

            var messagesDeleted = 0;
            foreach(var coop in coops) {
                var channel = (SocketTextChannel)await _discord.GetChannelAsync(coop.DiscordChannelId);

                if(channel is not null) {
                    var messages = await channel.GetMessagesAsync().FlattenAsync();

                    var messagesToDeleted = messages.Where(x => x.CreatedAt > DateTimeOffset.Now.AddHours(-10) && x.Author.IsBot && x.Content.Contains("Demerit added to"));
                    if(messagesToDeleted.Any()) {
                        Console.WriteLine($"Deleting {messages.Count()} messages from {coop.Name}");
                        messagesDeleted += messagesToDeleted.Count();
                        await channel.DeleteMessagesBatchAsync(messagesToDeleted);
                    }

                }
            }

            return Json(messagesDeleted);
        }

        private static async Task<Ei.SaveBackupResponse> SubmitBackup(Ei.Backup backup) {
            var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
            using(var client = new HttpClient(handler)) {
                client.BaseAddress = new Uri("https://www.auxbrain.com/");

                var ms1 = new MemoryStream();
                backup.WriteTo(ms1);
                //Serializer.Serialize<FirstContactRequestProto>(ms1, new FirstContactRequestProto { UserId = UserId, P2 = 0, P3 = 2 });
                ms1.Position = 0;
                var messageData = ms1.ToArray();
                var ms2 = new MemoryStream();
                new Ei.AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = ContractsAPI.GetHash(messageData) }.WriteTo(ms2);

                ms2.Position = 0;
                var sr = new StreamReader(ms2);
                var base64 = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(sr.ReadToEnd()));
                var bac = new ByteArrayContent(ASCIIEncoding.ASCII.GetBytes("data=" + base64));
                client.DefaultRequestHeaders.Add("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)");
                client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                bac.Headers.Add("Content-Type", "application/x-www-form-urlencoded");

                HttpResponseMessage response;

                //try {
                response = await client.PostAsync("ei/save_backup_secure ", bac);
                //} catch(Exception) {
                //    await Task.Delay(500);
                //    response = await client.PostAsync("ei/first_contact", bac);
                //}

                string r;
                if(response.IsSuccessStatusCode) {
                    r = await response.Content.ReadAsStringAsync();
                    var responseString = System.Convert.FromBase64String(await response.Content.ReadAsStringAsync());


                    var backupR = ContractsAPI.GetFromAuthenticatedMessage<Ei.SaveBackupResponse>(responseString);


                    //backup.Success = true;
                    return backupR;
                }
            }
            return null;
        }

        public async Task<IActionResult> GetMessage() {
            var message = await _discord.Guilds.First(x => x.Id == 656455567858073601).GetTextChannel(933047117621100605).GetMessageAsync(933395126749896804);
            return Json(message, new JsonSerializerOptions { ReferenceHandler = ReferenceHandler.IgnoreCycles });
        }

        public async Task<IActionResult> CheckBoost() {
            var channel = _discord.GetGuild(656455567858073601).GetTextChannel(680431628950044676);
            var msg = await channel.GetMessageAsync(847572559913549874);
            return Json(msg);
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]

        [Produces("application/xml")]
        public async Task<IActionResult> XmlOut(string ei) {
            //var rawBackup = await ContractsAPI.FirstContact(ei);
            //var backup = new CustomBackup(rawBackup.Backup);
            var backup = await _apiLink.GetBackup(ei);
            return new ObjectResult(backup);
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Produces("application/json")]
        public async Task<IActionResult> JsonOut(string ei) {
            var backup = await _apiLink.GetBackup(ei);
            return new ObjectResult(backup);
        }
        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Produces("application/json")]
        public async Task<IActionResult> RawJsonOut(string ei) {
            var backup = await ContractsAPI.FirstContact(ei);
            return new ObjectResult(backup);
        }

        public async Task<IActionResult> CleanCoopPins() {
            var coops = await _db.Coops.AsQueryable().Where(x => x.DiscordChannelId != 0 && !x.DeletedChannel).ToListAsync();

            var retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync(new[]{
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(3)
                });

            var rnd = new Random();
            foreach(var guildGroup in coops.GroupBy(x => x.OverflowGuildId > 0 ? x.OverflowGuildId : x.GuildId)) {
                //var guild = await _discord.Rest.GetGuildAsync(guildGroup.Key);
                var guild = _discord.Guilds.FirstOrDefault(x => x.Id == guildGroup.Key);

                foreach(var coop in guildGroup.OrderBy(x => rnd.Next())) {
                    Console.Write(coop.Name);
                    var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId ?? "[]");
                    var channel = guild.GetTextChannel(coop.DiscordChannelId);
                    if(channel == null) {
                        Console.WriteLine($" Unable to find channel");
                        continue;
                    }
                    try {
                        var pinned = await channel.GetMessagesAsync(1000).FlattenAsync(); //await retryPolicy.ExecuteAsync(async () => );
                        Console.WriteLine(pinned.Count(x => x.IsPinned));
                        foreach(var msg in pinned.Where(x => x.Author.Id == 514257192803893272)) {
                            if(msg.IsPinned || msg.Embeds.Count > 0) {
                                if(!UpdateMessageIDs.Contains(msg.Id)) {
                                    await msg.DeleteAsync();
                                    Console.Write("X");
                                } else {
                                    Console.Write("_");
                                }
                            }
                        }
                    } catch(Exception e) {
                        Console.Write(e.Message);
                    }
                    Console.WriteLine("");
                }

            }
            return Content("Success");
        }



        //public async Task<IActionResult> CheckSize() {
        //    var user = await _db.Users.AsQueryable().FirstAsync(x => x.DiscordId == 248865520756064257);
        //    var response = await ContractsAPI.FirstContactRaw(user.EggIncIds.First().Id);

        //    Console.WriteLine(response.Response.Error);
        //    var jsonBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response.Response.Backup));
        //    var jsonZip = JsonHelper.Zip(jsonBytes);

        //    JsonHelper.CleanBackup(response.Response.Backup);

        //    var jsonBytes2 = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(response.Response.Backup, new JsonSerializerSettings { ContractResolver = new IgnoreHasResolver() }));
        //    var jsonZip2 = JsonHelper.Zip(jsonBytes2);
        //    var rawZip = JsonHelper.Zip(response.Raw);

        //    var ms = new MemoryStream();
        //    response.Response.Backup.WriteTo(ms);
        //    ms.Position = 0;
        //    var RawSize2 = ms.ToArray();
        //    var rawZip2 = JsonHelper.Zip(RawSize2);
        //    return Json(new {
        //        JsonSize = jsonBytes.Length,
        //        JsonZip = jsonZip.Length,
        //        RawSize = response.Raw.Length,
        //        Rawip = rawZip.Length,
        //        JsonSize2 = jsonBytes2.Length,
        //        JsonZip2 = jsonZip2.Length,
        //        RawSize2 = RawSize2.Length,
        //        rawZip2 = rawZip2.Length
        //    })
        //    ;
        //}



        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddAdminRole() {
            //var dbuser = await _db.Users.First(x => x.DiscordId == "");
            var user = await _userManager.FindByIdAsync(_db.UserLogins.First(x => x.ProviderKey == "689298717081468973").UserId);
            //var user = await _userManager.FindByIdAsync(_db.First(x => x.ema).UserId);
            //await _userManager.AddToRoleAsync(user, "GuildAdmin");
            //await _roleManager.CreateAsync(new IdentityRole("GuildLesserAdmin"));
            return Content("Success");
        }

        [EnableCors("SiteCorsPolicy")]
        public IActionResult XFinity([FromQuery] string usage) {
            Console.WriteLine(usage);

            var client = new HttpClient();
            client.GetStringAsync("https://nr.dev.sglade.com/endpoint/xfinity/" + usage);
            return Content("Success");
        }

        [EnableCors("SiteCorsPolicy")]
        public IActionResult XFinityMobile([FromQuery] string usage) {
            Console.WriteLine(usage);

            var client = new HttpClient();
            client.GetStringAsync("https://nr.dev.sglade.com/endpoint/xfinitymobile/" + usage);
            return Content("Success");
        }


        public IActionResult Index() {
            return View();
        }

        public IActionResult Privacy() {
            return View();
        }

        public async Task<IActionResult> CheckDiscord() {
            ViewBag.Discord = _discord;
            return View(await _db.DBUsers.AsQueryable().ToListAsync());
        }

        public async Task<IActionResult> UpdateDiscord() {
            var Model = await _db.DBUsers.AsQueryable().ToListAsync();
            foreach(var user in Model.Where(x => x.Registered < DateTimeOffset.Now.AddDays(-14))) {
                var guilds = _discord.Guilds.Where(x => x.Users.Any(y => y.Id == user.DiscordId));
                if(user.GuildId == 0 && guilds.Count() == 1) {
                    user.GuildId = guilds.First().Id;
                } else if(user.GuildId > 0 && guilds.Count() == 0) {
                    user.GuildId = 0;
                }
            }
            await _db.SaveChangesAsync();
            return Redirect("/home/checkdiscord");
        }

        public IActionResult ClearCookies() {
            foreach(var cookie in Request.Cookies.Keys) {
                Response.Cookies.Delete(cookie);
            }
            return View("Index");
        }

        public async Task<List<LeaderboardUser>> _getLeaderboard(ulong guildid) {
            var dbguild = await _db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildid);

            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(dbguild.InactiveElites ?? "[]");
            inactiveUsers.AddRange(JsonConvert.DeserializeObject<List<GuildUser>>(dbguild.InactiveStandards ?? "[]"));

            var rawusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == guildid && !x.TempDisabled).Select(x => new {
                x.DiscordId,
                x.DiscordUsername,
                x.GuildId,
                x.Id,
                x._CustomBackups,
                x._eggIncIds,
                x.Registered,
                //                    Contracts = x.UserCoopXrefs.Select(y => y.Coop.ContractID),
                DBUser = x
            }).ToListAsync();
            rawusers = rawusers.Where(x => !inactiveUsers.Any(y => y.DatabaseId == x.Id)).ToList();
            //var users = rawusers.Select(x => new DBUser {
            //    DiscordId = x.DiscordId,
            //    DiscordUsername = x.DiscordUsername,
            //    GuildId = x.GuildId,
            //    Id = x.Id,
            //    _CustomBackups = x._CustomBackups,
            //    _eggIncIds = x._eggIncIds,
            //    CreateOn = x.CreateOn, 
            //});

            //var clack = users.FirstOrDefault(x => x.DiscordId == 760260503124967426);
            //var users = await _db.Users.AsQueryable().Where(x => ).ToListAsync();

            var accounts = rawusers.SelectMany(x => x.DBUser.EggIncAccounts.Select(y => new LeaderboardUser {
                User = x.DBUser,
                Backup = y.Backup,
                DiscordUser = _discord.Guilds.First(g => g.Id == x.GuildId).Users.FirstOrDefault(du => du.Id == x.DiscordId),
                TotalContracts = x.DBUser.GuildCoops
            })).Where(x => x.DiscordUser != null && x.Backup != null && x.Backup.Farms.Count > 0).OrderByDescending(x => x.Backup.EarningsBonus).ToList();

            return accounts;
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Authorize]
        public async Task<IActionResult> Leaderboard([FromQuery] bool all = false, [FromQuery] bool oldest = false, [FromQuery] string sortby = "", [FromQuery] ulong guildid = 0) {
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            ViewBag.Oldest = oldest;
            ViewBag.SortBy = sortby;

            if(guildid == 0 || !User.IsInRole("Admin")) {
                guildid = user.GuildId;
            }

            await _discord.Guilds.First(x => x.Id == guildid).DownloadUsersAsync();

            var leaderboard = await _getLeaderboard(guildid);


            if(oldest) {
                return View(leaderboard.Where(x => x.Backup.PermitLevel == 0 && x.User.EggIncAccounts.Count == 1).OrderBy(x => x.User.Registered).ToList());
            } else {
                switch(sortby) {
                    case "se":
                        leaderboard = leaderboard.OrderByDescending(x => x.Backup.SoulEggs).ToList();
                        break;
                    case "pe":
                        leaderboard = leaderboard.OrderByDescending(x => x.Backup.EggsOfProphecy).ToList();
                        break;
                    case "start":
                        var firstContract = new DateTimeOffset(2018, 03, 24, 0, 0, 0, TimeSpan.Zero);
                        leaderboard.ForEach(x => x.Started = (x.Backup.ArchivedFarms?.Count ?? 0) > 0 ? x.Backup.ArchivedFarms.Where(x => x.Started > firstContract).Min(y => y.Started) : x.Backup.Farms.Min(y => y.Started));
                        leaderboard = leaderboard.OrderBy(x => x.Started).ToList();
                        break;
                    case "permit":
                        leaderboard = leaderboard.OrderByDescending(x => x.Backup.PermitLevel).ToList();
                        break;
                }
                return View(leaderboard);
            }
        }

        [Authorize]
        public async Task<IActionResult> EggDayLeaderboard([FromQuery] bool all = false, [FromQuery] bool oldest = false, [FromQuery] string sortby = "", [FromQuery] ulong guildid = 0, [FromQuery] int prefix = 0) {


            var timings = new TimingsFactory(_logger).Start();

            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            ViewBag.Oldest = oldest;
            ViewBag.SortBy = sortby;

            if(guildid == 0 || !User.IsInRole("Admin")) {
                guildid = user.GuildId;
            }


            if(!_cache.TryGetValue($"EGL{guildid}", out List<EggDayResults> results)) {
                var users = await _db.DBUsers.Where(x => x.GuildId == guildid && !x.TempDisabled).ToListAsync();

                timings.Set("Users");
                var accounts = users.SelectMany(u => u.EggIncAccounts.Select(a => new UserByAccount {
                    User = u,
                    Account = a,
                })).ToList();


                var eggincids = accounts.Select(x => x.Account.Id).ToList();


                var eggDayDate = new DateTime(2023, 07, 14, 11, 0, 0);
                var postEggDaySnapshots = await _db.UserSnapShots.AsQueryable().Where(x => eggincids.Contains(x.EggIncID) && x.Date > eggDayDate).GroupBy(x => x.EggIncID).Select(x => x.OrderBy(y => y.Date).First()).ToListAsync();
                timings.Set("postEggDaySnapshots");
                var preEggDaySnapshots = await _db.UserSnapShots.AsQueryable().Where(x => eggincids.Contains(x.EggIncID) && x.Date < eggDayDate).GroupBy(x => x.EggIncID).Select(x => x.OrderByDescending(y => y.Date).First()).ToListAsync();
                timings.Set("preEggDaySnapshots");


                results = postEggDaySnapshots.Select(x => {
                    var user = accounts.First(y => y.Account.Id == x.EggIncID);
                    var pre = preEggDaySnapshots.FirstOrDefault(y => y.EggIncID == x.EggIncID);
                    if(pre is null)
                        return null;

                    return new EggDayResults {
                        UserAccount = user,
                        EBGain = x.EarningsBonus - pre.EarningsBonus,
                        EBGainPercent = (x.EarningsBonus - pre.EarningsBonus) / pre.EarningsBonus,
                        SEGain = x.SoulEggs - pre.SoulEggs,
                        SEGainPercent = (x.SoulEggs - pre.SoulEggs) / pre.SoulEggs,
                        PrestigeCount = x.Prestiges - pre.Prestiges,
                        StartEB = pre.EarningsBonus
                    };
                }).Where(x => x is not null).ToList();
                _cache.Set($"EGL{guildid}", results, TimeSpan.FromMinutes(5));
            }



            results = results.OrderByDescending(x => x.EBGain).ToList();


            switch(sortby) {
                case "prestige":
                    results = results.OrderByDescending(x => x.PrestigeCount).ToList();
                    break;
                case "se":
                    results = results.OrderByDescending(x => x.SEGain).ToList();
                    break;
                case "seper":
                    results = results.OrderByDescending(x => x.SEGainPercent).ToList();
                    break;
                case "ebper":
                    results = results.OrderByDescending(x => x.EBGainPercent).ToList();
                    break;
                default:
                    results = results.OrderByDescending(x => x.EBGain).ToList();
                    break;
            }


            if(prefix > 0) {
                results = results.Where(x => SIPrefix.GetPrefixFromEB(x.StartEB).Base == prefix).ToList();
            }

            ViewBag.sortby = sortby;
            ViewBag.prefix = prefix;

            return View(results.ToList());

            //if(oldest) {
            //    return View(leaderboard.Where(x => x.Backup.PermitLevel == 0 && x.User.EggIncAccounts.Count == 1).OrderBy(x => x.User.Registered).ToList());
            //} else {
            //    switch(sortby) {
            //        case "se":
            //            leaderboard = leaderboard.OrderByDescending(x => x.Backup.SoulEggs).ToList();
            //            break;
            //        case "pe":
            //            leaderboard = leaderboard.OrderByDescending(x => x.Backup.EggsOfProphecy).ToList();
            //            break;
            //        case "start":
            //            var firstContract = new DateTimeOffset(2018, 03, 24, 0, 0, 0, TimeSpan.Zero);
            //            leaderboard.ForEach(x => x.Started = (x.Backup.ArchivedFarms?.Count ?? 0) > 0 ? x.Backup.ArchivedFarms.Where(x => x.Started > firstContract).Min(y => y.Started) : x.Backup.Farms.Min(y => y.Started));
            //            leaderboard = leaderboard.OrderBy(x => x.Started).ToList();
            //            break;
            //    }
            //    return View(leaderboard);
            //}
        }

        public class EggDayResults {
            public UserByAccount UserAccount { get; set; }
            public double EBGain { get; set; }
            public double SEGain { get; set; }
            public double EBGainPercent { get; set; }
            public double SEGainPercent { get; set; }
            public double StartEB { get; set; }
            public ulong PrestigeCount { get; set; }
        }

        public async Task<IActionResult> Results([FromQuery] bool all = false, [FromQuery] bool oldest = false, [FromQuery] string sortby = "") {
            if(User.IsInRole("Admin") || User.IsInRole("GuildAdmin") || true) {


                var snapshots = (await _db.UserSnapShots.AsQueryable().Where(x => x.Date < new DateTime(2021, 07, 14)).OrderByDescending(x => x.Date).ToListAsync()).GroupBy(x => x.EggIncID).Select(x => x.First()).ToList();
                ViewBag.Snapshots = snapshots;
                ViewBag.Oldest = oldest;
                ViewBag.SortBy = sortby;

                var guild = await _db.Guilds.AsQueryable().FirstAsync();

                var leaderboard = await _getLeaderboard(guild.Id);


                if(oldest) {
                    return View(leaderboard.Where(x => x.Backup.PermitLevel == 0 && x.User.EggIncAccounts.Count == 1).OrderBy(x => x.User.Registered).ToList());
                } else {
                    switch(sortby) {
                        case "se":
                            leaderboard = leaderboard.OrderByDescending(x => x.Backup.SoulEggs).ToList();
                            break;
                        case "start":
                            var firstContract = new DateTimeOffset(2018, 03, 24, 0, 0, 0, TimeSpan.Zero);
                            leaderboard.ForEach(x => x.Started = (x.Backup.ArchivedFarms?.Count ?? 0) > 0 ? x.Backup.ArchivedFarms.Where(x => x.Started > firstContract).Min(y => y.Started) : x.Backup.Farms.Min(y => y.Started));
                            leaderboard = leaderboard.OrderBy(x => x.Started).ToList();
                            break;
                    }
                    return View(leaderboard);
                }
            } else {
                return View("TempDisabled");
            }
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Authorize]
        public async Task<IActionResult> Enlightenment() {
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
            var leaderboard = await _getLeaderboard(user.GuildId);

            return View(leaderboard);
        }

        public async Task<IActionResult> EnlightenmentTest() {
            var guild = await _db.Guilds.AsQueryable().FirstAsync();
            var leaderboard = await _getLeaderboard(guild.Id);

            return View("Enlightenment", leaderboard);
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Produces("application/xml")]
        public async Task<IActionResult> LeaderboardXML(ulong guildid) {
            var users = await _getLeaderboard(guildid);
            var leaderboard = users.Select(x => new LeaderboardItem {
                Name = x.DiscordUser.GetCleanName(),
                EggIncName = x.Backup.UserName,
                SoulEggs = x.Backup.SoulEggs,
                EggsOfProphecy = x.Backup.EggsOfProphecy,
                ProPermit = x.Backup.PermitLevel == 1
            });
            return new ObjectResult(leaderboard);
        }
        public class LeaderboardItem {
            public string Name { get; set; }
            public string EggIncName { get; set; }
            public double SoulEggs { get; set; }
            public ushort EggsOfProphecy { get; set; }
            public bool ProPermit { get; set; }
        }

        [Authorize]
        public async Task<IActionResult> Comparison() {

            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            await _discord.Guilds.First(x => x.Id == user.GuildId).DownloadUsersAsync();

            var leaderboard = await _getLeaderboard(user.GuildId);

            var myEbsWithRole = new List<Tuple<double, string>>();
            var allEbData = new List<Tuple<double, string>>();

            foreach(var u in leaderboard) {
                // Add all users data.
                allEbData.Add(new Tuple<double, string>(
                    u.Backup.EarningsBonus,
                    SIPrefix.GetPrefixFromEB(u.Backup.EarningsBonus).RankWithSubRank
                    ));

                // Add logged in users data.
                if(u.User.Id == user.Id) {
                    myEbsWithRole.Add(new Tuple<double, string>(
                        u.Backup.EarningsBonus,
                        SIPrefix.GetPrefixFromEB(u.Backup.EarningsBonus).RankWithSubRank
                    ));
                }
            }

            ViewBag.ListOfEb = allEbData;
            ViewBag.MyEbs = myEbsWithRole;
            ViewBag.AllRoles = SIPrefix.GetAllFarmerRoles();

            return View();
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> ViewUser(Guid id) {
            var user = await _db.DBUsers.Include(x => x.UserCoopXrefs).ThenInclude(x => x.Coop).FirstOrDefaultAsync(x => x.Id == id);
            return RedirectToAction("ViewUser", "MyFarms", new { discordId = user.DiscordId });
        }

        public async Task<IActionResult> ViewUserId(string id) {
            var user = new DBUser {
                UserCoopXrefs = new List<UserCoopXref>()
            };
            var backup = await _apiLink.GetBackup(id);
            user.EggIncAccounts = new List<EggIncAccount> { new EggIncAccount { Backup = backup } };
            user.DiscordUsername = backup.UserName;
            //return Json(response);
            return View("ViewUser", user);
        }

        public async Task<IActionResult> ViewBackup(string id) {
            var user = await _db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.Id.ToString() == id || x.DiscordUsername == id);
            return Json(user.EggIncAccounts.Select(x => x.Backup));
        }

        public IActionResult Embed(string returnUrl) {
            return View((object)returnUrl);
        }

        public async Task<IActionResult> Coop([FromRoute] string ContractId, [FromRoute] string CoopId) {
            CoopId = CoopId.ToLower();
            ContractId = ContractId.ToLower();
            var model = new CoopModel {
                
                DbCoop = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.ContractID == ContractId && EF.Functions.Like(x.Name, CoopId)),
                Contract = await _db.Contracts.AsQueryable().FirstOrDefaultAsync(x => x.ID == ContractId)
            };
            model.CoopStatus = await ContractsAPI.GetCoopStatus(ContractId, CoopId.ToLower(), xrefs: model.DbCoop?.UserCoopsXrefs ?? new List<UserCoopXref>());

            if(model.CoopStatus.Participants.Any(x => x.UserName == "[departed]")) {
                var cd = new CoopDetails(model.DbCoop, model.Contract, model.DbCoop?.League ?? (uint)model.CoopStatus.Grade,
                model.DbCoop?.UserCoopsXrefs.SelectMany(y => y.User.EggIncAccounts.Select(b => new UserWithBackup { Backup = b.Backup, User = y.User })).ToList() ?? new List<UserWithBackup>(), _discord, model.CoopStatus);

                var missing = cd.CoopParticipants.Where(x => !x.Joined).ToList();
                var departed = model.CoopStatus.Participants.Where(x => x.UserName == "[departed]").ToList();
            }

            model.UserInfos = new List<CoopUserInfo>();

            if(model.CoopStatus == null) {
                model.CoopStatus = model.DbCoop.LastStatusUpdate;
            }

            var backupsNeeded = model.CoopStatus.Contributors.ToList();
            if(model.DbCoop != null) {
                var existingBackups = model.DbCoop.UserCoopsXrefs.SelectMany(xref => xref.User.EggIncAccounts.Where(b => b.Id == xref.EggIncId || b.Id == xref.RefEggIncId).Select(x => x.Backup)
                .Select(b => new CoopUserInfo {
                    Contribution = model.CoopStatus.Contributors.FirstOrDefault(c => c.UserName == b.UserName),
                    Backup = b,
                    Farm = b.Farms.FirstOrDefault(f => f.CoopId == CoopId),
                    Xref = xref
                }));

                model.UserInfos.AddRange(existingBackups.Where(x => x.Contribution != null));
                backupsNeeded = backupsNeeded.Where(x => !existingBackups.Any(y => y.Contribution?.UserId == x.UserId)).ToList();
                model.League = model.DbCoop.League;
            } else {
                model.League = (uint)model.CoopStatus.Grade;
            }
            var backups = await _apiLink.GetUserBackups(backupsNeeded.Select(x => x.UserId), new CancellationToken(), true);
            model.UserInfos.AddRange(backups.Select(b => new CoopUserInfo {
                Contribution = model.CoopStatus.Contributors.First(c => c.UserId == b.EggIncId),
                Backup = b,
                Farm = b.Farms.FirstOrDefault(f => f.CoopId == CoopId)
            }));

            model.UserInfos.AddRange(model.CoopStatus.Contributors.Where(x => !model.UserInfos.Any(y => x.UserId == y.Contribution?.UserId)).Select(x => new CoopUserInfo {
                Contribution = x
            }));

            if(model.Contract.Details == null) {
                var firstContact = await ContractsAPI.FirstContact(model.UserInfos.Where(x => x.Backup != null).First().Backup.EggIncId);
                var contract = firstContact.Backup.Contracts.Archive.First(c => c.Contract.Identifier == ContractId);
                model.Contract._response = JsonConvert.SerializeObject(contract.Contract);
                await _db.SaveChangesAsync();
            }



            var goals = model.Contract.Details.GetGoals((int)model.League);
            model.GoalDetails = goals.Select(goal => {
                var detail = new GoalDetails {
                    Goal = goal,
                    TimeLeft = Prefarm.GetTimeRemainingValue(goal.TargetAmount, model.CoopStatus.Contributors.Sum(c => c.ContributionRate), model.CoopStatus.TotalAmount),
                    Progress = model.CoopStatus.TotalAmount / goal.TargetAmount
                };
                if(detail.TimeLeft.TotalSeconds < 0) {
                    detail.Status = GoalStatus.Completed;
                } else if(detail.TimeLeft.TotalSeconds < model.CoopStatus.SecondsRemaining) {
                    detail.Status = GoalStatus.Achievable;
                } else if(detail.TimeLeft == TimeSpan.MaxValue) {
                    detail.Status = GoalStatus.Never;
                } else {
                    detail.Status = GoalStatus.NotAchievable;
                }
                return detail;
            }).ToList();

            model.Progress = Math.Min(1, model.CoopStatus.TotalAmount / goals.Last().TargetAmount);


            var timeLeft = Math.Max(0, Math.Min(model.GoalDetails.Last().TimeLeft.TotalSeconds, model.CoopStatus.SecondsRemaining));
            model.UserInfos.ForEach(x => x.Projected = x.Contribution.ContributionAmount + x.Contribution.ContributionRate * timeLeft);
            model.UserInfos.ForEach(x => x.ProjectedAbsolute = x.Contribution.ContributionAmount + x.Contribution.ContributionRate * model.CoopStatus.SecondsRemaining);
            var projected = model.UserInfos.Sum(x => x.Projected);
            model.UserInfos.ForEach(x => x.Share = x.Projected / projected);

            model.CoopDetails = new CoopDetails(model.DbCoop, model.Contract, model.DbCoop?.League ?? (uint)model.CoopStatus.Grade,
                model.DbCoop?.UserCoopsXrefs.SelectMany(y => y.User.EggIncAccounts.Select(b => new UserWithBackup { Backup = b.Backup, User = y.User })).ToList() ?? new List<UserWithBackup>(), _discord, model.CoopStatus);

            return View(model);
        }

        private uint GetLeague(List<CoopUserInfo> userInfos, string CoopId, string ContractId) {
            var farms = userInfos.SelectMany(x => x.Backup?.Farms.Where(y => y.CoopId == CoopId) ?? new List<CustomFarm>());
            if(farms.Count() > 0 && farms.Any(f => f.League == 1))
                return 1;
            var archivedFarms = userInfos.SelectMany(x => x.Backup?.ArchivedFarms.Where(y => y.CoopId == CoopId) ?? new List<CustomArchivedFarms>());
            if(archivedFarms.Count() > 0 && farms.Any(f => f.League == 1))
                return 1;
            archivedFarms = userInfos.SelectMany(x => x.Backup?.ArchivedFarms.Where(y => y.ContractId == ContractId) ?? new List<CustomArchivedFarms>());
            if(archivedFarms.Count() > 0 && farms.Any(f => f.League == 1))
                return 1;
            if(userInfos.All(ui => ui.Backup?.Farms.Any(f => f.League == 1 && f.ContractId == ContractId) ?? false)) {
                return 1;
            }
            return 0;
        }

        public class CoopModel {
            public Ei.ContractCoopStatusResponse CoopStatus { get; set; }
            public Coop DbCoop { get; set; }
            public Contract Contract { get; set; }
            public List<CoopUserInfo> UserInfos { get; set; }
            public uint League { get; set; }
            public List<GoalDetails> GoalDetails { get; set; }
            public double Progress { get; set; }
            public CoopDetails CoopDetails { get; set; }
        }

        public class CoopUserInfo {
            public Ei.ContractCoopStatusResponse.Types.ContributionInfo Contribution { get; set; }
            public CustomBackup Backup { get; set; }
            public CustomFarm Farm { get; set; }
            public UserCoopXref Xref { get; set; }
            public double Projected { get; set; }
            public double Share { get; set; }
            public double ProjectedAbsolute { get; set; }
        }

        public class GoalDetails {
            public Ei.Contract.Types.Goal Goal { get; set; }
            public GoalStatus Status { get; set; }
            public double Progress { get; set; }
            public TimeSpan TimeLeft { get; set; }
        }

        public enum GoalStatus {
            Completed,
            Achievable,
            NotAchievable,
            Never
        }

        public async Task<IActionResult> CheckChannels() {
            var channels = _discord.GetGuild(656455567858073601).TextChannels.Where(x => x.CategoryId.HasValue && x.Category.Name.ToLower().Contains("coops"));
            var text = new StringBuilder();
            foreach(var channel in channels) {
                var msgs = await channel.GetMessagesAsync(5).FlattenAsync();
                if(msgs.Count() == 0) {
                    text.Append($"{channel.Name}<br>");
                    await channel.DeleteAsync();
                }
            }

            return Content(text.ToString());
        }

        public IActionResult Boosts() {
            return View();
        }

        //public async Task<IActionResult> Test() {
        //    var coop = "cooptesting" + DateTime.Now.ToString("yyyyMMddhhmmss");
        //    //var coopBase = "testingblah";
        //    //var last = 0;
        //    //for (var i = 131072; i > 10000; i--) {
        //    //    var response = await ContractsAPI.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(new Ei.CreateCoopRequest {
        //    //        ClientVersion = 30,
        //    //        ContractIdentifier = "mday-brunch",
        //    //        CoopIdentifier = coopBase + i,
        //    //        League = 0,
        //    //        Platform = Aux.Platform.Ios,
        //    //        SecondsRemaining = i,// 28800 * 4.6,
        //    //        SoulPower = 24.24559831915049,
        //    //        UserId = "G:1008118781",
        //    //        UserName = "Kendrome"
        //    //    });
        //    //    var r2 = await ContractsAPI.GetCoopStatus("mday-brunch", coopBase + i);
        //    //    if(r2.SecondsRemaining < 100) {
        //    //        Debug.WriteLine(i);
        //    //        last = i;
        //    //        break;

        //    //    }
        //    //}

        //    //var request = new Ei.CreateCoopRequest {
        //    //    ClientVersion = 30,
        //    //    ContractIdentifier = "mday-brunch",
        //    //    CoopIdentifier = coop,
        //    //    League = 0,
        //    //    Platform = Aux.Platform.Ios,
        //    //    SecondsRemaining = 131071,// 28800 * 4.6,
        //    //    SoulPower = 24.24559831915049,
        //    //    UserId = "G:1008118781",
        //    //    UserName = "Kendrome"
        //    //};
        //    //var ms1 = new MemoryStream();
        //    //var outCodedStream = new CodedOutputStream(ms1);
        //    //request.WriteTo(ms1);
        //    //ms1.Position = 0;

        //    //var o = $"<{BitConverter.ToString(ms1.ToArray()).Replace("-", " ")}>";
        //    //return Json(o);

        //    //var response = await ContractsAPI.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(request);
        //    //var r2 = await ContractsAPI.GetCoopStatus("mday-brunch", coop);
        //    //return Json(r2);

        //    //var responseString = System.Convert.FromBase64String("CgttZGF5LWJydW5jaBINdGVzdHRlc3RoYWhnYRkAAPm8OjjQQCIMRzoxMDA4MTE4NzgxKghLZW5kcm9tZTABOBpB203o1W7KEBIAA==");

        //    //var ms = new MemoryStream();
        //    //ms.Write(responseString);
        //    //ms.Position = 0;

        //    //var res = Ei.ContractCoopStatusUpdateRequest.Parser.ParseFrom(ms);

        //    //var response = await ContractsAPI.Post<Ei.JoinCoopResponse, Ei.JoinCoopRequest>(new Ei.JoinCoopRequest {
        //    //    ClientVersion = 25,
        //    //    ContractIdentifier = "terraform-heavy",
        //    //    CoopIdentifier = "flockblush54",
        //    //    League = 0,
        //    //    Platform = Aux.Platform.Ios,
        //    //    SoulPower = 5,
        //    //    UserId = "G:1008118781",
        //    //    UserName = "kendrome"
        //    //});

        //    //var guildContract = await _db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == "all-or-nothing" && x.GuildID == 656455567858073601 && x.Elite);
        //    //var coop = await _db.Coops.FirstAsync(x => x.Id == Guid.Parse("C4B67152-B18A-4B3C-1679-08D8000F87C2"));

        //    //var request = new Ei.CreateCoopRequest();
        //    //request.ClientVersion = 25;
        //    //    request.ContractIdentifier = guildContract.ContractID;
        //    //    request.CoopIdentifier = coop.Name.ToLower();
        //    //    request.League = (uint)(guildContract.Elite ? 0 : 1);
        //    //    request.Platform = Aux.Platform.Ios;
        //    //    request.SecondsRemaining = guildContract.Contract.Details.LengthSeconds;
        //    //    request.SoulPower = guildContract.Elite ? 24.24559831915049 : 8.75;
        //    //    request.UserId = "G:1008118781";
        //    //    request.UserName = "Kendrome";

        //    //var response = await ContractsAPI.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(request);

        //    //var r = await ContractsAPI.Send<Ei.LeaveCoopRequest>(new Ei.LeaveCoopRequest {
        //    //    ClientVersion = 25,
        //    //    ContractIdentifier = coop.ContractID,
        //    //    CoopIdentifier = coop.Name,
        //    //    PlayerIdentifier = "G:1008118781"
        //    //});
        //}

        public async Task<IActionResult> Test1() {
            var userStatuses = await _db.UserCoopStatuses.AsQueryable().Where(x => x.CoopId == Guid.Parse("9C515840-2651-4FB2-CAB5-08D7FD8FF968")).OrderByDescending(x => x.CreatedOn).ToListAsync();
            return Json(userStatuses);
        }

        //public async Task<IActionResult> Test2() {
        //    var userStatuses = await _db.Coops.Where(x => x.Id == Guid.Parse("9C515840-2651-4FB2-CAB5-08D7FD8FF968")) .Select(x => x.user )
        //    return Json(userStatuses);
        //}


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public IActionResult Invite() {
            return Redirect("https://discord.gg/cluckinghampalace");
        }

        [ResponseCache(Duration = 600)]
        public async Task<ActionResult> EasterEggHunt() {
            var links = @"1 https://discord.com/channels/656455567858073601/656455568353132546/964162211155173406
16 https://discord.com/channels/656455567858073601/656455568353132546/963900393782411274
17 https://discord.com/channels/656455567858073601/656455568353132546/963853170247884830
18 https://discord.com/channels/656455567858073601/656455568353132546/963490534557638687
19 https://discord.com/channels/656455567858073601/656455568353132546/963274839647453254
20 https://discord.com/channels/656455567858073601/656455568353132546/963068327243178075
46 https://discord.com/channels/656455567858073601/656455568353132546/964300075604017183

💬suggestions-feedback 
2 https://discord.com/channels/656455567858073601/708071623571538021/944682495583072286
47 https://discord.com/channels/656455567858073601/708071623571538021/958173773624913980

👥talk-to-staff 
3 https://discord.com/channels/656455567858073601/746509501271769210/963763144306589696
21 https://discord.com/channels/656455567858073601/746509501271769210/963102979446169630
22 https://discord.com/channels/656455567858073601/746509501271769210/962141807846752317
23 https://discord.com/channels/656455567858073601/746509501271769210/961930312110194708
48 https://discord.com/channels/656455567858073601/746509501271769210/962762492839350393
49 https://discord.com/channels/656455567858073601/746509501271769210/963467041069760512

📦artifact-discussion 
4 https://discord.com/channels/656455567858073601/798985476006084628/964060031261736980
24 https://discord.com/channels/656455567858073601/798985476006084628/963147729813512192
25 https://discord.com/channels/656455567858073601/798985476006084628/963007467690807316
26 https://discord.com/channels/656455567858073601/798985476006084628/962933363117789194
50 https://discord.com/channels/656455567858073601/798985476006084628/964198432547962880

🖥egg9000-development 
5 https://discord.com/channels/656455567858073601/801134122838786078/943172562588938270
🎲off-topic 
6 https://discord.com/channels/656455567858073601/664563280081059845/959903201744793690
27 https://discord.com/channels/656455567858073601/664563280081059845/958143369756950598
28 https://discord.com/channels/656455567858073601/664563280081059845/958005557036466206
29 https://discord.com/channels/656455567858073601/664563280081059845/957356956677464106

⌚other-idle-games 
7 https://discord.com/channels/656455567858073601/816422628720902194/958821735086575676
30 https://discord.com/channels/656455567858073601/816422628720902194/959308464834904084
31 https://discord.com/channels/656455567858073601/816422628720902194/959821617163305040
32 https://discord.com/channels/656455567858073601/816422628720902194/959967738669985892

🍳food-and-snacks 
8 https://discord.com/channels/656455567858073601/792940901777014784/954436301128609902
33 https://discord.com/channels/656455567858073601/792940901777014784/955125561984970762

🎼music 
9 https://discord.com/channels/656455567858073601/793591029353676851/959189698830553169
34 https://discord.com/channels/656455567858073601/793591029353676851/959186566427844658

📟tech-and-games 
10 https://discord.com/channels/656455567858073601/793576356083793971/959170212933042216
35 https://discord.com/channels/656455567858073601/793576356083793971/960609181302411384
36 https://discord.com/channels/656455567858073601/793576356083793971/963682458509926430

📚books-and-tv 
11 https://discord.com/channels/656455567858073601/793836057702432799/960201900337295390
37 https://discord.com/channels/656455567858073601/793836057702432799/959497885647507606
38 https://discord.com/channels/656455567858073601/793836057702432799/958936101517672469
39 https://discord.com/channels/656455567858073601/793836057702432799/957544876545814538
😻pets 
12 https://discord.com/channels/656455567858073601/793657823379980318/958647605267685426
40 https://discord.com/channels/656455567858073601/793657823379980318/956457860965990410
41 https://discord.com/channels/656455567858073601/793657823379980318/955558252530253864
42 https://discord.com/channels/656455567858073601/793657823379980318/954935783792468028

🌄sports-and-outdoors 
13 https://discord.com/channels/656455567858073601/823901567039700992/961757739846078474
43 https://discord.com/channels/656455567858073601/823901567039700992/960752268553121823
44 https://discord.com/channels/656455567858073601/823901567039700992/960320628777439294
45 https://discord.com/channels/656455567858073601/823901567039700992/959510295552872538

🎨arts-and-crafts 
14 https://discord.com/channels/656455567858073601/821545853805920286/945298822811222036

📰world-news 
15 https://discord.com/channels/656455567858073601/947948999128789042/948973482681696316";

            var easterCacheKey = $"EasterEggs";
            Dictionary<RestGuildUser, int> eggsFound;
            if(!_cache.TryGetValue(easterCacheKey, out eggsFound)) {


                var regex = new Regex(@"(\d+)/(\d+)/(\d+)");
                var matches = regex.Matches(links);
                eggsFound = new Dictionary<RestGuildUser, int>();
                foreach(Match match in matches) {
                    var guild = await _discord.Rest.GetGuildAsync(ulong.Parse(match.Groups[1].Value));
                    var channel = await guild.GetTextChannelAsync(ulong.Parse(match.Groups[2].Value));
                    var message = await channel.GetMessageAsync(ulong.Parse(match.Groups[3].Value));
                    var reactions = message.Reactions;
                    var userReactions = await message.GetReactionUsersAsync(reactions.First(x => x.Key.Name.Contains("EASTER")).Key, 9999).FlattenAsync();
                    foreach(var user in userReactions) {
                        if(user.Username == "TreeGoat")
                            continue;
                        var existingUser = eggsFound.Any(x => x.Key.Id == user.Id);
                        if(existingUser) {
                            eggsFound[eggsFound.First(x => x.Key.Id == user.Id).Key]++;
                        } else {
                            var guildUser = await guild.GetUserAsync(user.Id);
                            eggsFound.Add(guildUser, 1);
                        }
                    }
                }
                _cache.Set(easterCacheKey, eggsFound, TimeSpan.FromMinutes(10));
            }
            return View(eggsFound);
        }
    }
}
