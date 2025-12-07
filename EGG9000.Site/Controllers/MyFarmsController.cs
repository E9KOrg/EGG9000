using Discord.WebSocket;

using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EIEpicResearch;
using EGG9000.Common.Services;
using EGG9000.Site.Services;

using Ei;

using Humanizer;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Event = EGG9000.Common.Database.Entities.Event;

namespace EGG9000.Site.Controllers {
    [Authorize]
    public class MyFarmsController(ILogger<MyFarmsController> logger, UserManager<IdentityUser> userManager, DiscordSocketClient discord,
        RoleManager<IdentityRole> roleManager, APILink apiLink, ApplicationDbContext db, Bugsnag.IClient bugsnag, IMemoryCache cache, DatabaseCache databaseCache) : Controller {

        private readonly ILogger<MyFarmsController> _logger = logger;
        private readonly ApplicationDbContext _db = db;
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;
        private readonly APILink _apiLink = apiLink;
        private readonly DiscordSocketClient _discord = discord;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;
        private readonly IMemoryCache _cache = cache;
        private readonly DatabaseCache _databaseCache = databaseCache;

        public async Task<IActionResult> Index() {
            var sw = new Stopwatch();
            sw.Start();
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);

            if(NewCoopChecker.WaitingOnCoops) {
                var weekAgo = DateTimeOffset.Now.AddDays(-7);
                var user = (await _databaseCache.GetDbUsers()).First(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
                var xrefs = await _db.UserCoopXrefs.Where(y => y.UserId == user.Id && y.CreatedOn > weekAgo && !y.Coop.Finished && !y.JoinedCoop).Include(y => y.Coop).ThenInclude(x => x.Contract).ToListAsync();
                user.UserCoopXrefs = xrefs;
                //var user = await _db.DBUsers.Include(x => x.UserCoopXrefs.Where(y => y.CreatedOn > weekAgo && !y.Coop.Finished && !y.JoinedCoop)).ThenInclude(y => y.Coop).ThenInclude(x => x.Contract).AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
                _logger.LogInformation($"Time: {sw.ElapsedMilliseconds}");
                return View("Temporary", user);
            }


            _bugsnag.Breadcrumbs.Leave($"DiscordId: {logins.First().ProviderKey}, {logins.First().ProviderDisplayName}");
            return await ViewUser(ulong.Parse(logins.First().ProviderKey));
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> ViewUser(ulong discordId) {


            Console.WriteLine("ViewUser");
            var times = new TimingsFactory(_logger);
            times.Start();




            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var loginUserId = ulong.Parse(logins.First().ProviderKey);
            var isSelf = loginUserId == discordId;
            var user = await _db.DBUsers.Include(x => x.UserCoopXrefs).ThenInclude(x => x.Coop).FirstOrDefaultAsync(x => x.DiscordId == discordId);
            _bugsnag.Breadcrumbs.Leave($"DiscordId: {discordId}");
            _bugsnag.Breadcrumbs.Leave($"DiscordUsername: {user.DiscordUsername}");
            //var rawBackups = new List<Ei.Backup>();
            var scoring = new List<(string EggIncId, MyContracts MyContracts)>();

            times.Set("User prep");


            var getBackupsTask = GetBackups(user, scoring);

            var contractIDs = user.EggIncAccounts.Where(x => x.Backup is not null).SelectMany(b => b.Backup.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId)).ToList();

            var Contracts = await _db.Contracts.AsQueryable().ToListAsync();

            var Demerits = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            var Merits = await _db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            /*var RawBackups = rawBackups;*/
            var Snapshots = await _db.UserSnapShots.AsQueryable().Where(x => x.UserId == user.Id).ToListAsync();
            var xrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => x.UserId == user.Id && !x.Coop.ThreadArchived && !x.Coop.DeletedChannel && !x.JoinedCoop).Include(x => x.Coop).ThenInclude(x => x.Contract).ToListAsync();
            var coops = await _db.Coops.Where(x => x.UserCoopsXrefs.Any(y => y.UserId == user.Id && y.JoinedCoop) && !x.ThreadArchived && !x.DeletedChannel).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).ToListAsync();
            var EpicResearchConfig = Root.Get().epicResearchItems;
            var DbGuild = await _db.Guilds.FirstOrDefaultAsync(x => x.Id == user.GuildId);
            var uncompletedPes = GetUncompletedPEContracts(user, Contracts);

            List<DBCustomEgg> dbCustomEggs = _cache.GetOrCreate("CustomEggsCache", entry => {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                return _db.CustomEggs.ToList();
            });

            //var dbCustomEggs = await _db.GetCustomEggsAsync();

            times.Set("Pre backups");
            var update = await getBackupsTask;

            if(update) {
                user.UpdateAccounts();
                Console.WriteLine("Saving updated backups to DB");
                Console.WriteLine(String.Join(",",_db.ChangeTracker.Entries().Where(x => x.State != EntityState.Unchanged).Select(x => x.Entity.GetType())));
                await _db.SaveChangesAsync();
            }

            times.Set("Post backups");


            Console.WriteLine(String.Join("\n", times.Finished().Select(y => $"{y.name}: {y.time.Humanize().ShortenTime()}")));
            return View("Index", new MyFarmsModel(user, Contracts, Demerits, Merits, /*RawBackups,*/ Snapshots, xrefs, coops, EpicResearchConfig, scoring, DbGuild, uncompletedPes, dbCustomEggs, isSelf));
        }

        private async Task<bool> GetBackups(DBUser user, List<(string EggIncId, MyContracts MyContracts)> scoring) {
            var update = false;

            foreach(var account in user.EggIncAccounts) {
                var rawBackup = await ContractsAPI.FirstContact(account.Id);
                //rawBackups.Add(rawBackup.Backup);
                var customBackup = new CustomBackup(rawBackup.Backup, account?.Backup ?? null);
                //var json = JsonSerializer.Serialize(customBackup);
                //var json = Newtonsoft.Json.JsonConvert.SerializeObject(customBackup);
                //var customBackupAfterJson = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomBackup>(json);

                //var response = await _apiLink.GetBackup(accounts.Id);
                Console.WriteLine($"Getting backups for {account.Name}");
                if(customBackup?.Farms is not null) {
                    account.Backup = customBackup;
                    update = true;
                }
                //Console.WriteLine(customBackup.SpaceMissions.Count);

                MyContracts scores = _cache.GetOrCreate($"{account.Id}-MyContracts", entry => {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return ContractsAPI.Post<MyContracts, BasicRequestInfo>(new BasicRequestInfo(), account.Id).GetAwaiter().GetResult();
                });

                //var scores = await ContractsAPI.Post<MyContracts, BasicRequestInfo>(new BasicRequestInfo(), account.Id);

                scoring.Add((account.Id, scores));
            }
            if(update) {
                user.UpdateAccounts();
            }
            return update;
        }

        public record MyFarmsModel(
            DBUser User,
            List<Common.Database.Entities.Contract> Contracts,
            List<Demerit> Demerits,
            List<Merit> Merits,
            /*List<Backup> RawBackups*/
            List<UserSnapShot> SnapShots,
            List<UserCoopXref> UnjoinedCoops,
            List<Coop> JoinedCoops,
            List<EpicResearchItem> EpicResearchConfig,
            List<(string EggIncId, MyContracts MyContracts)> Scoring,
            Guild DBGuild,
            Dictionary<string, List<Common.Database.Entities.Contract>> UncompletedPEContracts,
            List<DBCustomEgg> CustomEggs,
            bool IsSelf
        );

        public async Task<IActionResult> EarningsBoostCalculator() {
            var loginuser = (await _userManager.GetUserAsync(User));

            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            //Get fresh backups
            foreach(var account in user.EggIncAccounts) {
                var backup = await _apiLink.GetBackup(account.Id);
                if(backup?.Farms is not null && backup.LastBackupTime > account.Backup.LastBackupTime) {
                    account.Backup = backup;
                }
            }

            var contractIDs = user.EggIncAccounts.SelectMany(b => b.Backup.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId)).ToList();
            ViewBag.Contracts = await _db.Contracts.AsQueryable().Where(x => contractIDs.Contains(x.ID)).ToListAsync();
            //user.Backups.ForEach(b => b.Contracts.Contracts.ToList().ForEach(c => c.Contract.Name = contracts.FirstOrDefault(x => x.ID == c.Contract.Identifier)?.Name));

            var boostEvent = await _db.Events.AsQueryable().Where(x => x.Type == "earnings-boost" && !x.Ended && x.Ends > DateTimeOffset.Now).FirstOrDefaultAsync();

            return View(new EarningsBoostCalculatorModel {
                Backup = user.EggIncAccounts.First().Backup,
                Event = boostEvent,
                CustomEggs = await _db.GetCustomEggsAsync()
            });
        }

        public class EarningsBoostCalculatorModel {
            public CustomBackup Backup { get; set; }
            public Event Event { get; set; }
            public List<DBCustomEgg> CustomEggs { get; set; }
        }

        public async Task<IActionResult> ResearchTest() {
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);

            var user = await _db.DBUsers.FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
            ViewBag.CustomEggs = await _db.GetCustomEggsAsync();
            ViewBag.ResearchCostSubmissions = await _db.ResearchCostSubmissions.ToListAsync();
            return View(user.EggIncAccounts.First().Backup);
        }

        public class SubmitResearchCostModel {
            public string Id { get; set; }
            public int Level { get; set; }
            public double Cost { get; set; }
        }
        public async Task<IActionResult> SubmitResearchCost([FromBody] SubmitResearchCostModel model) {
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
            var existing = await _db.ResearchCostSubmissions.FirstOrDefaultAsync(x => x.ID == model.Id && x.Level == model.Level && x.UserId == user.Id);
            if(existing is null) {
                existing = new ResearchCostSubmission {
                    ID = model.Id,
                    Level = model.Level,
                    Cost = model.Cost,
                    UserId = user.Id,
                };
                _db.ResearchCostSubmissions.Add(existing);
            } else {
                existing.Cost = model.Cost;
                existing.SubmittedAt = DateTimeOffset.Now;
            }
            await _db.SaveChangesAsync();
            return Ok();
        }

        public async Task<IActionResult> Roles() {
            var roles = await _userManager.GetRolesAsync(await _userManager.GetUserAsync(User));
            return Json(roles);
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> RemoveDemerit([FromQuery] Guid id) {
            var demerit = _db.Demerit.FirstOrDefault(x => x.Id == id);
            _db.Remove(demerit);
            await _db.SaveChangesAsync();
            return Redirect(Request.Headers["Referer"].ToString());
        }

        public Dictionary<string, List<Common.Database.Entities.Contract>> GetUncompletedPEContracts(DBUser user, List<Common.Database.Entities.Contract> contracts) {
            return user.EggIncAccounts.ToDictionary(
                account => account.Id,
                account => account.Backup.ArchivedFarms
                    .Where(f =>
                        f.PEPossible > 0 && f.PEGained < f.PEPossible
                    )
                    .Select(f => contracts.FirstOrDefault(c => c.ID == f.ContractId.ToLower()))
                    .Concat(contracts.Where(c => c.Details.GetPossiblePE() > 0 && !account.Backup.ArchivedFarms.Any(f => f.ContractId == c.ID)))
                    .Where(x => x is not null)
                    .ToList()
            );
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> RemoveMerit([FromQuery] Guid id) {
            var merit = _db.Merit.FirstOrDefault(x => x.Id == id);
            _db.Remove(merit);
            await _db.SaveChangesAsync();
            return Redirect(Request.Headers["Referer"].ToString());
        }

        public async Task<IActionResult> SendTestDM([FromQuery] string target, [FromQuery] ulong discorduserid) {
            switch(target) {
                case "dm":
                    var discordUser = await _discord.GetUserAsync(discorduserid);
                    await DiscordHelpersExt.BoolSendDm(discordUser, "Testing DM Ping", _db);
                    return Ok();
                case "talktoegg9000":
                    var channel = (SocketTextChannel)_discord.GetChannel(1012791664831639613);
                    await channel.SendMessageAsync($"Testing Ping for <@{discorduserid}>");
                    return Ok();
            }
            return BadRequest();
        }

        public async Task<IActionResult> CoopOptimizer([FromQuery] Guid CoopId) {
            var coop = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == CoopId);
            var customEggs = await _db.GetCustomEggsAsync();
            return View((coop, customEggs));
        }
    }
}