using Discord;
using Discord.Rest;
using Discord.WebSocket;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Site.Auth;
using EGG9000.Site.Models;
using EGG9000.Site.Models.Home;
using EGG9000.Site.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Controllers {
    public partial class HomeController(ILogger<HomeController> logger, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, SignInManager<ApplicationUser> signInManager,
        DiscordSocketClient discord, ApplicationDbContext db, IMemoryCache cache, DatabaseCache databaseCache) : E9KControllerBase {

        private readonly ILogger<HomeController> _logger = logger;
        private readonly ApplicationDbContext _db = db;
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;
        private readonly DiscordSocketClient _discord = discord;
        private readonly IMemoryCache _cache = cache;
        private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
        private readonly DatabaseCache _databaseCache = databaseCache;

        [AllowAnonymous]
        public async Task<IActionResult> DebugLogin([FromQuery] string id) {
            if(!BuildConfig.IsDebug && !BuildConfig.IsDev9002) return NotFound();

            var a = await _db.UserLogins.FirstOrDefaultAsync(x => x.ProviderKey == id);
            var user = await _userManager.Users.FirstAsync(x => x.Id == a.UserId);
            var dbuser = await _db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == ulong.Parse(id));
            if(dbuser.GuildId != 1108127105088241746) {
                return NotFound();
            }
            await _signInManager.SignInWithClaimsAsync(user, true, [
                new("DbUserId", dbuser.Id.ToString()),
                new("DiscordId", id),
                new("GuildId", dbuser.GuildId.ToString())
            ]);
            return Redirect("/");
        }
        [AllowAnonymous]
        public async Task<IActionResult> Alive() {
            var contract = await _db.Contracts.FirstAsync();
            return Content("Success");
        }

        [AllowAnonymous]
        public IActionResult AliveDiscord() {

            if(_discord.ConnectionState == ConnectionState.Connected)
                return Content("Success");
            else return StatusCode(503);
        }

        [Authorize(Roles = "Admin,GuildLesserAdmin,GuildAdmin")]
        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Produces("application/xml")]
        public async Task<IActionResult> XmlOut(string ei) {
            var (backup, _) = await EggIncApi.GetBackupAsync(ei, await _db.CachedEiContractsAsync());
            return new ObjectResult(backup);
        }

        [Authorize(Roles = "Admin,GuildLesserAdmin,GuildAdmin")]
        [ResponseCache(Duration = 360, VaryByQueryKeys = ["*"])]
        [Produces("application/json")]
        public async Task<IActionResult> JsonOut(string ei) {
            var (backup, _) = await EggIncApi.GetBackupAsync(ei, await _db.CachedEiContractsAsync());
            return new ObjectResult(backup);
        }

        [Authorize(Roles = "Admin,GuildLesserAdmin,GuildAdmin")]
        [ResponseCache(Duration = 360, VaryByQueryKeys = ["*"])]
        [Produces("application/json")]
        public async Task<IActionResult> RawJsonOut(string ei) {
            var backup = await EggIncApi.FirstContact(ei);
            return new ObjectResult(backup);
        }

        [Authorize(Roles = "Admin,GuildLesserAdmin,GuildAdmin")]
        [ResponseCache(Duration = 360, VaryByQueryKeys = ["*"])]
        [Produces("application/json")]
        public async Task<IActionResult> CustomBackupOut(string ei) {
            var rawBackup = await EggIncApi.FirstContact(ei);
            var customBackup = new CustomBackup(rawBackup.Backup, await _db.CachedEiContractsAsync());
            return Json(customBackup);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CleanCoopPins() {
            var coops = await _db.Coops.Where(x => x.ThreadID != 0 && !x.ThreadArchived).ToListAsync();

            var retryPolicy = Policy.Handle<Exception>().WaitAndRetryAsync([
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(3)
            ]);

            var rnd = new Random();
            foreach(var guildGroup in coops.GroupBy(x => x.OverflowGuildId > 0 ? x.OverflowGuildId : x.GuildId)) {
                var guild = _discord.Guilds.FirstOrDefault(x => x.Id == guildGroup.Key);

                foreach(var coop in guildGroup.OrderBy(x => rnd.Next())) {
                    var UpdateMessageIDs = JsonConvert.DeserializeObject<List<ulong>>(coop.UpdateMessagesId ?? "[]");
                    var channel = coop.ThreadID != 0 ? guild.GetThreadChannel(coop.ThreadID) : guild.GetTextChannel(coop.DiscordChannelId);
                    if(channel == null) {
                        continue;
                    }
                    try {
                        var pinned = await channel.GetMessagesAsync(1000).FlattenAsync();
                        Console.WriteLine(pinned.Count(x => x.IsPinned));
                        foreach(var msg in pinned.Where(x => x.Author.Id == KnownUsers.Bot)) {
                            if(msg.IsPinned || msg.Embeds.Count > 0) {
                                if(!UpdateMessageIDs.Contains(msg.Id)) {
                                    await msg.DeleteAsync();
                                }
                            }
                        }
                    } catch(Exception) { }
                }

            }
            return Content("Success");
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddAdminRole() {
            var user = await _userManager.FindByIdAsync(_db.UserLogins.First(x => x.ProviderKey == "689298717081468973").UserId);
            return Content("Success");
        }

        [AllowAnonymous]
        public IActionResult Index() {
            return View();
        }

        [AllowAnonymous]
        public IActionResult Privacy() {
            return View();
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CheckDiscord() {
            ViewBag.Discord = _discord;
            return View(await _db.DBUsers.AsNoTracking().ToListAsync());
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateDiscord() {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
            var Model = await _db.DBUsers.Where(x => x.Registered < cutoff).ToListAsync();
            foreach(var user in Model) {
                var guilds = _discord.Guilds.Where(x => x.Users.Any(y => y.Id == user.DiscordId));
                if(user.GuildId == 0 && guilds.Count() == 1) {
                    user.GuildId = guilds.First().Id;
                } else if(user.GuildId > 0 && guilds.Count() == 0) {
                    var assigned = _discord.Guilds.FirstOrDefault(x => x.Id == user.GuildId);
                    if(assigned is not null && assigned.HasAllMembers) {
                        user.LastGuild = user.GuildId;
                        user.GuildId = 0;
                    }
                }
            }
            await _db.SaveChangesAsync();
            return Redirect("/home/checkdiscord");
        }

        [AllowAnonymous]
        public IActionResult ClearCookies() {
            foreach(var cookie in Request.Cookies.Keys) {
                Response.Cookies.Delete(cookie);
            }
            return View("Index");
        }

        public async Task<List<LeaderboardUser>> _getLeaderboard(ulong guildid) {
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guildid);

            var guild = _discord.Guilds.First(g => g.Id == guildid);
            // Membership is the DB GuildId, not the live Discord cache. The site runs its own bare
            // socket client whose member cache can read "complete" while actually partial, and gating
            // on it dropped real members from the board (the recurring CSLeaderboard "few users" bug).
            // ManageOverflow owns reconciling GuildId against true Discord membership; the board just
            // trusts it. DiscordUser is still resolved below for the display name only.
            var allUsers = await _databaseCache.GetDbUsers();
            var rawusers = allUsers.Where(x => x.GuildId == guildid && !x.TempDisabled);

            var accounts = rawusers.SelectMany(dbu => dbu.EggIncAccounts.Select(y => new LeaderboardUser {
                User = dbu,
                Backup = y.Backup,
                DiscordUser = guild.Users.FirstOrDefault(du => du.Id == dbu.DiscordId),
                TotalContracts = dbu.GuildCoops,
                TotalCS = y.Backup?.TotalCS ?? 0,
                SeasonCS = y.Backup?.SeasonCS ?? 0,
                TotalCraftingXP = y.Backup?.CraftingXP ?? 0,
                CraftingLevel = y.Backup?.GetCraftingLevel() ?? 1,
            })).Where(x => x.Backup != null && x.Backup.Farms.Count > 0 && (x.Account.Active || guildid == 1108127105088241746)).OrderByDescending(x => x.Backup.EarningsBonus).ToList();

            return accounts;
        }

        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Authorize]
        public async Task<IActionResult> Leaderboard([FromQuery] bool all = false, [FromQuery] bool oldest = false, [FromQuery] string sortby = "", [FromQuery] ulong guildid = 0) {
            if(NewCoopChecker.WaitingOnCoops) {
                return View("LeaderboardTemporaryDown");
            }
            ViewBag.Oldest = oldest;
            ViewBag.SortBy = sortby;

            var leaderboard = (await ResolveGuildLeaderboardAsync(guildid)).Board;

            if(oldest) {
                return View(leaderboard.Where(x => x.Backup.PermitLevel == 0 && x.User.EggIncAccounts.Count == 1).OrderBy(x => x.User.Registered).ToList());
            } else {
                switch(sortby) {
                    case "se":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.SoulEggs)];
                        break;
                    case "pe":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.EggsOfProphecy)];
                        break;
                    case "start":
                        var firstContract = new DateTimeOffset(2018, 03, 24, 0, 0, 0, TimeSpan.Zero);
                        leaderboard.ForEach(x => x.Started = (x.Backup.ArchivedFarms?.Count ?? 0) > 0 ? x.Backup.ArchivedFarms.Where(x => x.Started > firstContract).Min(y => y.Started) : x.Backup.Farms.Min(y => y.Started));
                        leaderboard = [.. leaderboard.OrderBy(x => x.Started)];
                        break;
                    case "permit":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.PermitLevel)];
                        break;
                    case "mer":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.MER)];
                        break;
                    case "eot":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.EggsOfTruth)];
                        break;
                    case "shifts":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.ShiftCount)];
                        break;
                    case "eott":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.EggsOfTruthTotal)];
                        break;
                    case "eov":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.VirtueEggsDelivered?.Sum() ?? 0)];
                        break;
                    case "tepershift":
                        leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.ShiftCount > 0 ? (double)x.Backup.EggsOfTruthTotal / (double)x.Backup.ShiftCount : 0)];
                        break;
                }
                return View(leaderboard);
            }
        }

        public async Task<IActionResult> FAQ() {
            var guildId = GetGuildId();
            var guild = await GetGuildAsync(guildId);

            var topics = await _db.QueryFAQTopicsAsync(guild, false, "");

            var model = new Home_FAQViewModel() {
                GuildName = guild.Name,
                FAQTopics = topics
            };

            return View(model);
        }

        public async Task<IActionResult> CraftingLevelLeaderboard([FromQuery] ulong guildid = 0) {
            var leaderboard = (await ResolveGuildLeaderboardAsync(guildid)).Board;
            leaderboard = [.. leaderboard.OrderByDescending(x => x.TotalCraftingXP).Where(x => x.TotalCraftingXP > 0)];
            return View(leaderboard);
        }

        public async Task<IActionResult> CSLeaderboard(string cstype = "total", [FromQuery] ulong guildid = 0) {
            ViewBag.CSType = cstype;

            var leaderboard = (await ResolveGuildLeaderboardAsync(guildid)).Board;

            switch(cstype) {
                case "season":
                    leaderboard = [.. leaderboard.OrderByDescending(x => x.SeasonCS).Where(x => x.SeasonCS > 0)];
                    break;
                case "total":
                default:
                    leaderboard = [.. leaderboard.OrderByDescending(x => x.TotalCS).Where(x => x.TotalCS > 0)];
                    break;
            }
            return View(leaderboard);
        }

        [Authorize]
        public async Task<IActionResult> EggDayLeaderboard([FromQuery] bool all = false, [FromQuery] bool oldest = false, [FromQuery] string sortby = "", [FromQuery] string year = "", [FromQuery] ulong guildid = 0, [FromQuery] int prefix = 0) {


            var timings = new TimingsFactory(_logger).Start();

            var user = await GetCurrentDbUserAsync();

            var maxYearInt = (DateTimeOffset.UtcNow.Month >= 7 && (DateTimeOffset.UtcNow.Month >= 8 || DateTimeOffset.UtcNow.Day >= 14)) ? DateTimeOffset.UtcNow.Year : (DateTimeOffset.UtcNow.Year - 1);
            if(!int.TryParse(year, out var yearInt)) {
                yearInt = maxYearInt;
            }
            if(yearInt >= DateTimeOffset.UtcNow.Year) {
                yearInt = maxYearInt;
            }

            var yearList = Enumerable.Range(2023, maxYearInt - 2023 + 1).ToList();

            ViewBag.Years = yearList;
            ViewBag.Year = yearInt;
            ViewBag.Oldest = oldest;
            ViewBag.SortBy = sortby;

            if(guildid == 0 || !User.IsInRole("Admin")) {
                guildid = user.GuildId;
            }


            var cacheKey = $"EGL{guildid}-{yearInt}";
            if(!_cache.TryGetValue(cacheKey, out List<Home_EggDayResults> results)) {
                var users = await _db.DBUsers.Where(x => x.GuildId == guildid && !x.TempDisabled).ToListAsync();

                timings.Set("Users");
                var accounts = users.SelectMany(u => u.EggIncAccounts.Select(a => new UserByAccount {
                    User = u,
                    Account = a,
                })).ToList();


                var eggincids = accounts.Select(x => x.Account.Id).ToList();


                var eggDayDate = new DateTimeOffset(yearInt, 07, 14, 11, 0, 0, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time").GetUtcOffset(DateTimeOffset.UtcNow));
                // Snapshots from 16th @ Midnight (after event is over)

                var preEggDaySnapshots = await _db.UserSnapShots.Where(x => eggincids.Contains(x.EggIncID) && x.Date < eggDayDate).GroupBy(x => x.EggIncID).Select(x => x.OrderByDescending(y => y.Date).First()).ToListAsync();
                timings.Set("preEggDaySnapshots");


                List<UserSnapShot> postEggDaySnapshots;
                if(DateTimeOffset.UtcNow.Date > eggDayDate && DateTimeOffset.UtcNow.Date < eggDayDate.AddDays(1)) {
                    postEggDaySnapshots = [.. accounts.Where(x => preEggDaySnapshots.Any(y => y.EggIncID == x.Account.Id)).Select(x => new UserSnapShot { EarningsBonus = x.Account.Backup.EarningsBonus, EggIncID = x.Account.Id, EggsOfProphecy = x.Account.Backup.EggsOfProphecy, Prestiges = x.Account.Backup.NumPrestiges, SoulEggs = x.Account.Backup.SoulEggs, UserId = x.User.Id, Date = DateTime.Now })];
                } else {
                    postEggDaySnapshots = await _db.UserSnapShots.Where(x => eggincids.Contains(x.EggIncID) && x.Date >= eggDayDate).GroupBy(x => x.EggIncID).Select(x => x.OrderBy(y => y.Date).First()).ToListAsync();
                }
                timings.Set("postEggDaySnapshots");
                // Snapshots from 14th @ Midnight (before event started)


                results = [.. postEggDaySnapshots.Select(x => {
                    var user = accounts.First(y => y.Account.Id == x.EggIncID);
                    var pre = preEggDaySnapshots.FirstOrDefault(y => y.EggIncID == x.EggIncID);
                    if(pre is null)
                        return null;

                    return new Home_EggDayResults {
                        UserAccount = user,
                        EBGain = x.EarningsBonus - pre.EarningsBonus,
                        EBGainPercent = (x.EarningsBonus - pre.EarningsBonus) / pre.EarningsBonus,
                        SEGain = x.SoulEggs - pre.SoulEggs,
                        SEGainPercent = (x.SoulEggs - pre.SoulEggs) / pre.SoulEggs,
                        PrestigeCount = x.Prestiges - pre.Prestiges,
                        StartEB = pre.EarningsBonus
                    };
                }).Where(x => x is not null)];
                _cache.Set(cacheKey, results, TimeSpan.FromMinutes(5));
            }



            results = [.. results.OrderByDescending(x => x.EBGain)];


            switch(sortby) {
                case "prestige":
                    results = [.. results.OrderByDescending(x => x.PrestigeCount)];
                    break;
                case "se":
                    results = [.. results.OrderByDescending(x => x.SEGain)];
                    break;
                case "seper":
                    results = [.. results.OrderByDescending(x => x.SEGainPercent)];
                    break;
                case "ebper":
                    results = [.. results.OrderByDescending(x => x.EBGainPercent)];
                    break;
                default:
                    results = [.. results.OrderByDescending(x => x.EBGain)];
                    break;
            }


            if(prefix > 0) {
                results = [.. results.Where(x => SIPrefix.GetPrefixFromEB(x.StartEB).Base == prefix)];
            }

            ViewBag.sortby = sortby;
            ViewBag.prefix = prefix;

            return View(results.ToList());

        }

        public async Task<IActionResult> Results([FromQuery] bool oldest = false, [FromQuery] string sortby = "") {
            if(User.IsInRole("Admin") || User.IsInRole("GuildAdmin")) {


                var snapshots = (await _db.UserSnapShots.Where(x => x.Date < new DateTime(2021, 07, 14)).OrderByDescending(x => x.Date).ToListAsync()).GroupBy(x => x.EggIncID).Select(x => x.First()).ToList();
                ViewBag.Snapshots = snapshots;
                ViewBag.Oldest = oldest;
                ViewBag.SortBy = sortby;

                var guild = await _db.Guilds.FirstAsync();

                var leaderboard = await _getLeaderboard(guild.Id);


                if(oldest) {
                    return View(leaderboard.Where(x => x.Backup.PermitLevel == 0 && x.User.EggIncAccounts.Count == 1).OrderBy(x => x.User.Registered).ToList());
                } else {
                    switch(sortby) {
                        case "se":
                            leaderboard = [.. leaderboard.OrderByDescending(x => x.Backup.SoulEggs)];
                            break;
                        case "start":
                            var firstContract = new DateTimeOffset(2018, 03, 24, 0, 0, 0, TimeSpan.Zero);
                            leaderboard.ForEach(x => x.Started = (x.Backup.ArchivedFarms?.Count ?? 0) > 0 ? x.Backup.ArchivedFarms.Where(x => x.Started > firstContract).Min(y => y.Started) : x.Backup.Farms.Min(y => y.Started));
                            leaderboard = [.. leaderboard.OrderBy(x => x.Started)];
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
            var user = await GetCurrentDbUserAsync();
            var leaderboard = await _getLeaderboard(user.GuildId);
            var customEggs = await _db.GetCustomEggsAsync();

            return View(new Home_EnlightenmentModel(leaderboard, customEggs));
        }


        [ResponseCache(Duration = 360, VaryByQueryKeys = new string[] { "*" })]
        [Produces("application/xml")]
        public async Task<IActionResult> LeaderboardXML(ulong guildid) {
            var users = await _getLeaderboard(guildid);
            var leaderboard = users.Select(x => new Home_LeaderboardItem {
                Name = x.DisplayName,
                EggIncName = x.Backup.UserName,
                SoulEggs = x.Backup.SoulEggs,
                EggsOfProphecy = x.Backup.EggsOfProphecy,
                ProPermit = x.Backup.PermitLevel == 1
            });
            return new ObjectResult(leaderboard);
        }

        [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName)]
        [HttpGet]
        public async Task<IActionResult> LeaderboardJson() {
            var guildId = GetGuildId();
            var guild = _discord.Guilds.FirstOrDefault(x => x.Id == guildId);
            if(guild == null) return StatusCode(503);
            await guild.DownloadUsersAsync();
            var leaderboard = await _getLeaderboard(guildId);
            var result = leaderboard.Select(x => new Home_LeaderboardApiItem {
                DiscordName = x.DisplayName,
                DiscordId = x.DisplayDiscordId,
                EggIncName = x.Backup.UserName,
                EarningsBonus = x.Backup.EarningsBonus,
                SoulEggs = x.Backup.SoulEggs,
                EggsOfProphecy = x.Backup.EggsOfProphecy,
                MER = x.Backup.MER,
                EggsOfTruth = x.Backup.EggsOfTruth,
                NumPrestiges = x.Backup.NumPrestiges
            }).ToList();
            return Json(result);
        }

        private async Task<(DBUser User, ulong Guildid, List<LeaderboardUser> Board)> ResolveGuildLeaderboardAsync(ulong guildid) {
            var user = await GetCurrentDbUserAsync();
            if(guildid == 0 || !User.IsInRole("Admin")) {
                guildid = user.GuildId;
            }
            await _discord.Guilds.First(x => x.Id == guildid).DownloadUsersAsync();
            return (user, guildid, await _getLeaderboard(guildid));
        }

        private async Task<(List<T> All, List<T> Mine, List<string> MyNames)> BuildComparison<T>(Func<LeaderboardUser, T> project, Func<IEnumerable<LeaderboardUser>, IEnumerable<LeaderboardUser>> filter = null) {
            var user = await GetCurrentDbUserAsync();
            await _discord.Guilds.First(x => x.Id == user.GuildId).DownloadUsersAsync();

            IEnumerable<LeaderboardUser> leaderboard = await _getLeaderboard(user.GuildId);
            if(filter is not null) leaderboard = filter(leaderboard);

            var all = new List<T>();
            var mine = new List<T>();
            var myNames = new List<string>();
            foreach(var u in leaderboard) {
                all.Add(project(u));
                if(u.User.Id == user.Id) {
                    mine.Add(project(u));
                    myNames.Add(u.Account.Name ?? u.Backup?.UserName ?? u.DiscordUser?.Username ?? u.User.DiscordUsername);
                }
            }
            return (all, mine, myNames);
        }

        [Authorize]
        public async Task<IActionResult> Comparison() {
            var (all, mine, myNames) = await BuildComparison(u => new Tuple<double, string>(
                u.Backup.EarningsBonus,
                SIPrefix.GetPrefixFromEB(u.Backup.EarningsBonus).RankWithSubRank));

            ViewBag.ListOfEb = all;
            ViewBag.MyEbs = mine;
            ViewBag.MyNames = myNames;
            ViewBag.AllRoles = SIPrefix.GetAllFarmerRoles();

            return View();
        }

        [Authorize]
        public async Task<IActionResult> GradeComparison() {
            var (all, mine, myNames) = await BuildComparison(u => new Tuple<int, double>(
                (int)(u?.Account?.LastGrade ?? Ei.Contract.Types.PlayerGrade.GradeUnset),
                u?.Backup?.TotalCS ?? 0));

            ViewBag.MyGradeData = mine;
            ViewBag.MyNames = myNames;
            ViewBag.AllGradeData = all;
            ViewBag.AllGrades = new List<int> { 0, 1, 2, 3, 4, 5 };

            return View();
        }

        [Authorize]
        public async Task<IActionResult> CraftingLevelComparison() {
            var (all, mine, myNames) = await BuildComparison(u => new Tuple<int, double>(
                (int)u?.Account?.Backup.GetCraftingLevel(),
                (double)(u?.Account?.Backup?.CraftingXP)),
                leaderboard => leaderboard.Where(x => x.TotalCraftingXP > 0));

            ViewBag.MyCraftingData = mine;
            ViewBag.MyNames = myNames;
            ViewBag.AllCraftingData = all;
            ViewBag.AllCraftingLevels = Enumerable.Range(1, 31).ToList();

            return View();
        }

        // Read tier: leaderboard name-links land here and redirect to MyFarms.ViewUser (also read tier).
        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin,GuildReadOnlyAdmin")]
        public async Task<IActionResult> ViewUser(Guid id) {
            var user = await _db.DBUsers.Include(x => x.UserCoopXrefs).ThenInclude(x => x.Coop).FirstOrDefaultAsync(x => x.Id == id);
            return RedirectToAction("ViewUser", "MyFarms", new { discordId = user.DiscordId });
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> ViewUserId(string id) {
            var user = new DBUser {
                UserCoopXrefs = []
            };
            var (backup, _) = await EggIncApi.GetBackupAsync(id, await _db.CachedEiContractsAsync());
            user.EggIncAccounts = [new() { Backup = backup }];
            user.DiscordUsername = backup.UserName;
            return View("ViewUser", user);
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> ViewBackup(string id) {
            var user = await _db.DBUsers.FirstOrDefaultAsync(x => x.Id.ToString() == id || x.DiscordUsername == id);
            if(user == null) {
                return NotFound();
            }
            return Json(user.EggIncAccounts.Select(x => x.Backup));
        }

        [AllowAnonymous]
        public IActionResult Embed(string returnUrl) {
            return View((object)returnUrl);
        }

        [AllowAnonymous]
        public async Task<IActionResult> Coop([FromRoute] string ContractId, [FromRoute] string CoopId) {
            CoopId = CoopId.ToLower();
            var model = new Home_CoopModel {

                DbCoop = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Include(x => x.Contract).FirstOrDefaultAsync(x => x.ContractID == ContractId && EF.Functions.ILike(x.Name, CoopId)),
                Contract = await _db.Contracts.FirstOrDefaultAsync(x => x.ID == ContractId),
                CustomEggs = await _db.GetCustomEggsAsync()
            };


            model.CoopStatus = await EggIncApi.GetCoopStatus(ContractId, CoopId.ToLower(), EIID: model.DbCoop?.CreatorID, xrefs: model.DbCoop?.UserCoopsXrefs ?? [], _logger: _logger);

            model.CoopStatus ??= model.DbCoop?.LastStatusUpdate;

            model.UserInfos = [];

            var backupsNeeded = model.CoopStatus.Contributors.ToList();
            if(model.DbCoop != null) {
                var existingBackups = model.DbCoop.UserCoopsXrefs.SelectMany(xref => xref.User.EggIncAccounts.Where(b => b.Id == xref.EggIncId || b.Id == xref.RefEggIncId).Select(x => x.Backup)
                .Select(b => new Home_CoopUserInfo {
                    Contribution = model.CoopStatus.Contributors.FirstOrDefault(c => c.UserName == b.UserName),
                    Backup = b,
                    Farm = b.Farms.FirstOrDefault(f => f.CoopId == CoopId),
                    Xref = xref
                }));

                model.UserInfos.AddRange(existingBackups.Where(x => x.Contribution != null));
                model.League = model.DbCoop.League;
            } else {
                model.League = (uint)model.CoopStatus.Grade;
            }

            model.UserInfos.AddRange(model.CoopStatus.Contributors.Where(x => !model.UserInfos.Any(y => x.UserId == y.Contribution?.UserId)).Select(x => new Home_CoopUserInfo {
                Contribution = x
            }));

            if(model.Contract.Details == null) {
                var firstContact = await EggIncApi.FirstContact(model.UserInfos.Where(x => x.Backup != null).First().Backup.EggIncId);
                var contract = firstContact.Backup.Contracts.Archive.First(c => c.Contract.Identifier == ContractId);
                model.Contract._response = JsonConvert.SerializeObject(contract.Contract);
                await _db.SaveChangesAsync();
            }



            var goals = model.Contract.Details.GetGoals((int)model.League);
            model.GoalDetails = [.. goals.Select(goal => {
                var detail = new Home_GoalDetails {
                    Goal = goal,
                    TimeLeft = GetTimeRemainingValue(goal.TargetAmount, model.CoopStatus.Contributors.Sum(c => c.ContributionRate), model.CoopStatus.TotalAmount),
                    Progress = model.CoopStatus.TotalAmount / goal.TargetAmount
                };
                if(detail.TimeLeft.TotalSeconds < 0) {
                    detail.Status = Home_GoalStatus.Completed;
                } else if(detail.TimeLeft.TotalSeconds < model.CoopStatus.SecondsRemaining) {
                    detail.Status = Home_GoalStatus.Achievable;
                } else if(detail.TimeLeft == TimeSpan.MaxValue) {
                    detail.Status = Home_GoalStatus.Never;
                } else {
                    detail.Status = Home_GoalStatus.NotAchievable;
                }
                return detail;
            })];

            model.Progress = Math.Min(1, model.CoopStatus.TotalAmount / goals.Last().TargetAmount);


            var timeLeft = Math.Max(0, Math.Min(model.GoalDetails.Last().TimeLeft.TotalSeconds, model.CoopStatus.SecondsRemaining));
            model.UserInfos.ForEach(x => x.Projected = x.Contribution.ContributionAmount + x.Contribution.ContributionRate * timeLeft);
            model.UserInfos.ForEach(x => x.ProjectedAbsolute = x.Contribution.ContributionAmount + x.Contribution.ContributionRate * model.CoopStatus.SecondsRemaining);
            var projected = model.UserInfos.Sum(x => x.Projected);
            model.UserInfos.ForEach(x => x.Share = x.Projected / projected);

            model.CoopDetails = new CoopDetails(model.DbCoop, model.Contract, model.DbCoop?.League ?? (uint)model.CoopStatus.Grade,
                model.DbCoop?.UserCoopsXrefs.SelectMany(y => y.User.EggIncAccounts.Select(b => new UserWithBackup { Backup = b.Backup, User = y.User })).ToList() ?? [], model.CustomEggs, _discord, model.CoopStatus);

            return View(model);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CheckChannels() {
            var channels = _discord.GetGuild(656455567858073601).TextChannels.Where(x => x.CategoryId.HasValue && x.Category.Name.Contains("coops", StringComparison.CurrentCultureIgnoreCase));
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

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [AllowAnonymous]
        public IActionResult Invite() {
            return Redirect("https://discord.gg/cluckinghampalace");
        }

        [ResponseCache(Duration = 600)]
        public async Task<ActionResult> HalloweenHunt() {
            var links = @"pets
1 https://discord.com/channels/656455567858073601/793657823379980318/1137531135858053221
2 https://discord.com/channels/656455567858073601/793657823379980318/1141451234763612290
3 https://discord.com/channels/656455567858073601/793657823379980318/1147190984992628807

ooo-pretty
4 https://discord.com/channels/656455567858073601/997065059870183454/1162256962088620052
5 https://discord.com/channels/656455567858073601/997065059870183454/1162440798730723549

talk-to-staff
6 https://discord.com/channels/656455567858073601/746509501271769210/1166964382111109172

general-discussion
7 https://discord.com/channels/656455567858073601/656455568353132546/1167860751856324759
8 https://discord.com/channels/656455567858073601/656455568353132546/1167866501575999488
9 https://discord.com/channels/656455567858073601/798985476006084628/1167897993354166342
10 https://discord.com/channels/656455567858073601/656455568353132546/1168016129906704424

ongoing-giveaway-discussion
11 https://discord.com/channels/656455567858073601/1094454621105307718/1158048460298268773

prestige-pointers
12 https://discord.com/channels/656455567858073601/1062265666817753128/1163240887556509706
13 https://discord.com/channels/656455567858073601/1062265666817753128/1167676193189920788

artifact-discussion
14 https://discord.com/channels/656455567858073601/798985476006084628/1167842583943319642
15 https://discord.com/channels/656455567858073601/798985476006084628/1167897993354166342

off-topic
16 https://discord.com/channels/656455567858073601/664563280081059845/1164837137343066154
17 https://discord.com/channels/656455567858073601/664563280081059845/1166072475847766146
18 https://discord.com/channels/656455567858073601/664563280081059845/1166187273033875508

food-and-snacks
19 https://discord.com/channels/656455567858073601/792940901777014784/1151297635311943762
20 https://discord.com/channels/656455567858073601/792940901777014784/1156413304558862357

tech-and-games
21 https://discord.com/channels/656455567858073601/793576356083793971/1160256688029454446
22 https://discord.com/channels/656455567858073601/793576356083793971/1162614595626745958

space-and-science
23 https://discord.com/channels/656455567858073601/796127648899530762/1084138285842059375
24 https://discord.com/channels/656455567858073601/796127648899530762/1095467290419527690

world-news
25 https://discord.com/channels/656455567858073601/947948999128789042/1080834875361329222

arts-and-crafts
26 https://discord.com/channels/656455567858073601/821545853805920286/1103947681228926986

sports-and-outdoors
27 https://discord.com/channels/656455567858073601/823901567039700992/1154002038649270312

books-and-tv
28 https://discord.com/channels/656455567858073601/793836057702432799/1149552277175144519

music
29 https://discord.com/channels/656455567858073601/793591029353676851/1130563391027675260";

            var easterCacheKey = $"HalloweenEggs";
            if(!_cache.TryGetValue(easterCacheKey, out Dictionary<RestGuildUser, int> eggsFound)) {
                eggsFound = await EggHuntScanner.CountReactionsAsync(_discord.Rest, links, "Hallowegg", user => user.Username == "melina8irbie");
                _cache.Set(easterCacheKey, eggsFound, TimeSpan.FromMinutes(10));
            }
            return View(eggsFound);
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
            if(!_cache.TryGetValue(easterCacheKey, out Dictionary<RestGuildUser, int> eggsFound)) {
                eggsFound = await EggHuntScanner.CountReactionsAsync(_discord.Rest, links, "EASTER", user => user.Username == "TreeGoat");
                _cache.Set(easterCacheKey, eggsFound, TimeSpan.FromMinutes(10));
            }
            return View(eggsFound);
        }
    }
}
