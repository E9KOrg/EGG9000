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
using EGG9000.Bot.Services;
using Polly;
using EGG9000.Common.Database;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Controllers {
    public class HomeController : Controller {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly DiscordSocketClient _discord;
        private readonly APILink _apiLink;

        public HomeController(
            ILogger<HomeController> logger,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            DiscordSocketClient discord,
            APILink apiLink,
            ApplicationDbContext db) {
            _discord = discord;
            _roleManager = roleManager;
            _userManager = userManager;
            _logger = logger;
            _apiLink = apiLink;
            _db = db;
        }


        public async Task<IActionResult> CheckBoost() {
            var channel = _discord.GetGuild(656455567858073601).GetTextChannel(680431628950044676);
            var msg = await channel.GetMessageAsync(847572559913549874);
            return Json(msg);
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]

        [Produces("application/xml")]
        public async Task<IActionResult> XmlOut(string ei) {
            var backup = await _apiLink.GetBackup(ei);
            //var xs = new System.Xml.Serialization.XmlSerializer(backup.GetType());
            //return new ObjectResult("Message me");
            return new ObjectResult(backup);
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Produces("application/json")]
        public async Task<IActionResult> JsonOut(string ei) {
            var backup = await _apiLink.GetBackup(ei);
            //var xs = new System.Xml.Serialization.XmlSerializer(backup.GetType());
            //return new ObjectResult("Message me");
            return new ObjectResult(backup);
        }

        public async Task<IActionResult> CleanCoopPins() {
            var coops = await _db.Coops.AsQueryable().Where(x => x.DiscordChannelId != 0 && !x.DeletedChannel && x.Status != CoopStatusEnum.WaitingOnStarter).ToListAsync();

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
                        var pinned = await channel.GetMessagesAsync(UpdateMessageIDs.First(), Direction.Before, 1000).FlattenAsync(); //await retryPolicy.ExecuteAsync(async () => );
                        foreach(var msg in pinned) {
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
            foreach(var user in Model.Where(x => x.CreateOn < DateTimeOffset.Now.AddDays(-14))) {
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

            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(dbguild.InactiveElites);
            inactiveUsers.AddRange(JsonConvert.DeserializeObject<List<GuildUser>>(dbguild.InactiveStandards));

            var rawusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == guildid && !x.TempDisabled).Select(x => new {
                x.DiscordId,
                x.DiscordUsername,
                x.GuildId,
                x.Id,
                x._CustomBackups,
                x._eggIncIds,
                x.CreateOn,
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

            var accounts = rawusers.Where(x => x.DBUser.Backups != null).SelectMany(x => x.DBUser.Backups.Select(y => new LeaderboardUser {
                User = x.DBUser,
                Backup = y,
                DiscordUser = _discord.Guilds.First(g => g.Id == x.GuildId).Users.FirstOrDefault(du => du.Id == x.DiscordId),
                TotalContracts = x.DBUser.GuildCoops
            })).Where(x => x.DiscordUser != null && x.Backup != null && x.Backup.Farms.Count > 0).OrderByDescending(x => x.Backup.EarningsBonus).ToList();

            return accounts;
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Authorize]
        public async Task<IActionResult> Leaderboard([FromQuery] bool all = false, [FromQuery] bool oldest = false, [FromQuery] string sortby = "") {
            if(User.IsInRole("Admin") || User.IsInRole("GuildAdmin") || true) {

                var loginuser = (await _userManager.GetUserAsync(User));
                var logins = await _userManager.GetLoginsAsync(loginuser);
                var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

                ViewBag.Oldest = oldest;
                ViewBag.SortBy = sortby;

                var leaderboard = await _getLeaderboard(user.GuildId);


                if(oldest) {
                    return View(leaderboard.Where(x => x.Backup.PermitLevel == 0 && x.User.EggIncIds.Count == 1).OrderBy(x => x.User.CreateOn).ToList());
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

        public async Task<IActionResult> Results([FromQuery] bool all = false, [FromQuery] bool oldest = false, [FromQuery] string sortby = "") {
            if(User.IsInRole("Admin") || User.IsInRole("GuildAdmin") || true) {


                var snapshots = (await _db.UserSnapShots.AsQueryable().Where(x => x.Date < new DateTime(2021, 07, 14)).OrderByDescending(x => x.Date).ToListAsync()).GroupBy(x => x.EggIncID).Select(x => x.First()).ToList();
                ViewBag.Snapshots = snapshots;
                ViewBag.Oldest = oldest;
                ViewBag.SortBy = sortby;

                var guild = await _db.Guilds.AsQueryable().FirstAsync();

                var leaderboard = await _getLeaderboard(guild.Id);


                if(oldest) {
                    return View(leaderboard.Where(x => x.Backup.PermitLevel == 0 && x.User.EggIncIds.Count == 1).OrderBy(x => x.User.CreateOn).ToList());
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


        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> ViewUser(Guid id) {
            var user = await _db.DBUsers.Include(x => x.UserCoopXrefs).ThenInclude(x => x.Coop).FirstOrDefaultAsync(x => x.Id == id);
            return RedirectToAction("ViewUser", "MyFarms", new { discordId = user.DiscordId });

            //var backups = new List<CustomBackup>();
            //foreach(var accounts in user.EggIncIds) {
            //    //var response = await _apiLink.GetBackup(accounts.Id);
            //    var response = await ContractsAPI.FirstContact(accounts.Id);
            //    if(response != null) {
            //        Console.WriteLine($"Got backup for: {accounts.Id}");
            //        var backup = new CustomBackup(response.Backup);
            //        backup.Farms.AddRange(response.Backup.Contracts.Archive.Select(y => new CustomFarm {
            //            CoopId = y.CoopIdentifier,
            //            TimeAccepted = (long)y.TimeAccepted,
            //            ContractId = y.Contract.Identifier
            //        }));
            //        backups.Add(backup);

            //    } else if(user.Backups.Any(x => x?.EggIncId == accounts.Id)) {
            //        Console.WriteLine($"Unable to get backup: {accounts.Id}");

            //        backups.Add(user.Backups.First(x => x.EggIncId == accounts.Id));
            //    } else {
            //        Console.WriteLine(JsonConvert.SerializeObject(response, Formatting.Indented));
            //    }
            //}
            //Console.WriteLine(JsonConvert.SerializeObject(user.EggIncIds, Formatting.Indented));
            //user.Backups = backups;
            ////await _db.SaveChangesAsync();
            //return View("~/Views/MyFarms/Index.cshtml", user);
        }

        public async Task<IActionResult> ViewUserId(string id) {
            var user = new DBUser {
                UserCoopXrefs = new List<UserCoopXref>()
            };
            var backups = new List<CustomBackup>();
            var response = await _apiLink.GetBackup(id);
            backups.Add(response);
            user.Backups = backups;
            user.DiscordUsername = response.UserName;
            //return Json(response);
            return View("ViewUser", user);
        }

        public async Task<IActionResult> ViewBackup(string id) {
            var user = await _db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.Id.ToString() == id || x.DiscordUsername == id);
            return Json(user.Backups);
        }

        public async Task<IActionResult> Coop([FromRoute] string ContractId, [FromRoute] string CoopId) {
            CoopId = CoopId.ToLower();
            ContractId = ContractId.ToLower();
            var model = new CoopModel {
                CoopStatus = await ContractsAPI.GetCoopStatus(ContractId, CoopId),
                DbCoop = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.ContractID == ContractId && x.Name == CoopId),
            };
            model.Contract = await _db.Contracts.AsQueryable().FirstOrDefaultAsync(x => x.ID == ContractId);

            model.UserInfos = new List<CoopUserInfo>();

            var backupsNeeded = model.CoopStatus.Contributors.ToList();
            if(model.DbCoop != null) {
                var existingBackups = model.DbCoop.UserCoopsXrefs.SelectMany(xref => xref.User.Backups.Where(b => b.EggIncId == xref.EggIncId || b.EggIncId == xref.RefEggIncId)
                .Select(b => new CoopUserInfo {
                    Contribution = model.CoopStatus.Contributors.FirstOrDefault(c => c.UserId == xref.EggIncId || c.UserId == xref.RefEggIncId),
                    Backup = b,
                    Farm = b.Farms.FirstOrDefault(f => f.CoopId == CoopId),
                    Xref = xref
                }));

                model.UserInfos.AddRange(existingBackups.Where(x => x.Contribution != null));
                backupsNeeded = backupsNeeded.Where(x => !existingBackups.Any(y => y.Contribution?.UserId == x.UserId)).ToList();
            }
            var backups = await _apiLink.GetUserBackups(backupsNeeded.Select(x => x.UserId), true);
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

            model.League = GetLeague(model.UserInfos, CoopId, ContractId);

            var goals = model.Contract.Details.GoalSets != null ? model.Contract.Details.GoalSets[(int)model.League].Goals : model.Contract.Details.Goals;
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
            return View(model);
        }

        private uint GetLeague(List<CoopUserInfo> userInfos, string CoopId, string ContractId) {
            var farms = userInfos.SelectMany(x => x.Backup.Farms.Where(y => y.CoopId == CoopId));
            if(farms.Count() > 0 && farms.Any(f => f.League == 1))
                return 1;
            var archivedFarms = userInfos.SelectMany(x => x.Backup.ArchivedFarms.Where(y => y.CoopName == CoopId));
            if(archivedFarms.Count() > 0 && farms.Any(f => f.League == 1))
                return 1;
            archivedFarms = userInfos.SelectMany(x => x.Backup.ArchivedFarms.Where(y => y.ContractId == ContractId));
            if(archivedFarms.Count() > 0 && farms.Any(f => f.League == 1))
                return 1;
            if(userInfos.All(ui => ui.Backup.Farms.Any(f => f.League == 1 && f.ContractId == ContractId))) {
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
    }
}
