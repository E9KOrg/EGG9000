using Discord.WebSocket;

using EGG9000.Common.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EIEpicResearch;
using EGG9000.Site.Services;

using Ei;

using Humanizer;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Event = EGG9000.Common.Database.Entities.Event;
using System.Collections.Frozen;
using EGG9000.Common.JsonData;

namespace EGG9000.Site.Controllers {
    [Authorize]
    public class MyFarmsController(ILogger<MyFarmsController> logger, UserManager<ApplicationUser> userManager, DiscordSocketClient discord,
        RoleManager<IdentityRole> roleManager, ApplicationDbContext db, Bugsnag.IClient bugsnag, IMemoryCache cache, DatabaseCache databaseCache,
        IServiceScopeFactory scopeFactory, EGG9000.Site.Services.ArtifactImageRenderer artifactRenderer) : Controller {

        private readonly ILogger<MyFarmsController> _logger = logger;
        private readonly ApplicationDbContext _db = db;
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly RoleManager<IdentityRole> _roleManager = roleManager;
        private readonly DiscordSocketClient _discord = discord;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;
        private readonly IMemoryCache _cache = cache;
        private readonly DatabaseCache _databaseCache = databaseCache;
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
        private readonly EGG9000.Site.Services.ArtifactImageRenderer _artifactRenderer = artifactRenderer;

        public async Task<IActionResult> Index() {
            var sw = new Stopwatch();
            sw.Start();
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);

            if(NewCoopChecker.WaitingOnCoops) {
                var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
                var user = (await _databaseCache.GetDbUsers()).First(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
                var xrefs = await _db.UserCoopXrefs.Where(y => y.UserId == user.Id && y.CreatedOn > weekAgo && !y.Coop.Finished && !y.JoinedCoop).Include(y => y.Coop).ThenInclude(x => x.Contract).ToListAsync();
                user.UserCoopXrefs = xrefs;
                _logger.LogInformation($"Time: {sw.ElapsedMilliseconds}");
                return View("Temporary", user);
            }


            _bugsnag.Breadcrumbs.Leave($"DiscordId: {logins.First().ProviderKey}, {logins.First().ProviderDisplayName}");
            return await ViewUser(ulong.Parse(logins.First().ProviderKey));
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin,GuildReadOnlyAdmin")]
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
            var scoring = new List<(string EggIncId, MyContracts MyContracts)>();

            times.Set("User prep");

            var cachedContracts = await _db.CachedEiContractsAsync();
            // MyContracts scores are cached (1h) and _db-free, so kick them off concurrently with the DB queries below.
            var scoresTask = GetScores(user, scoring);

            var Contracts = await _db.Contracts.AsQueryable().ToListAsync();

            var Demerits = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            var Merits = await _db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            var Snapshots = await _db.UserSnapShots.AsQueryable().Where(x => x.UserId == user.Id).ToListAsync();
            var xrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => x.UserId == user.Id && !x.Coop.ThreadArchived && !x.JoinedCoop && !x.Coop.Finished && x.Coop.CoopEnds > DateTime.UtcNow).Include(x => x.Coop).ThenInclude(x => x.Contract).ToListAsync();
            var coops = await _db.Coops.Where(x => x.UserCoopsXrefs.Any(y => y.UserId == user.Id && y.JoinedCoop) && !x.ThreadArchived).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).AsSplitQuery().ToListAsync();
            var erItems = EiEpicResearch.Get().epicResearchItems;
            var DbGuild = await _db.Guilds.FirstOrDefaultAsync(x => x.Id == user.GuildId);
            var uncompletedPes = GetUncompletedPEContracts(user, Contracts);

            var eggIncIds = user.EggIncAccounts.Select(a => a.Id).Distinct().ToList();
            var seasonProgresses = await _db.UserSeasonProgresses
                .Where(x => eggIncIds.Contains(x.EggIncId))
                .ToListAsync();
            // Every started season that awards PE, not only the ones the player already has progress in.
            // A season the player never touched has no UserSeasonProgress row, but they can still be
            // missing all of its PE - default that case to 0 CXP at the account's current grade.
            var seasonInfos = (await _db.SeasonInfos.ToListAsync())
                .Where(x => x.StartTime <= DateTimeOffset.UtcNow)
                .ToList();
            var seasonPEByEggIncId = new Dictionary<string, (int Earned, int Max)>();
            var missingSeasonalPEByEggIncId = new Dictionary<string, List<MissingSeasonalPe>>();
            foreach(var account in user.EggIncAccounts.DistinctBy(a => a.Id)) {
                var id = account.Id;
                var earned = 0;
                var max = 0;
                var missing = new List<MissingSeasonalPe>();
                foreach(var info in seasonInfos) {
                    var sp = seasonProgresses.FirstOrDefault(x => x.EggIncId == id && x.SeasonId == info.Id);
                    var totalCxp = sp?.TotalCxp ?? 0;
                    var grade = sp is not null
                        ? (Ei.Contract.Types.PlayerGrade)sp.StartingGrade
                        : account.GetGrade();
                    if(grade == Ei.Contract.Types.PlayerGrade.GradeUnset) continue;
                    earned += info.GetPeEarned(grade, totalCxp);
                    max += info.GetMaxPe(grade);
                    foreach(var goal in info.GetUnearnedGoals(grade, totalCxp))
                        missing.Add(new MissingSeasonalPe(info.Name, totalCxp, goal.Cxp, goal.PeAmount, info.StartTime));
                }
                seasonPEByEggIncId[id] = (Earned: earned, Max: max);
                missingSeasonalPEByEggIncId[id] = missing.OrderBy(m => m.StartTime).ToList();
            }

            var dbCustomEggs = _cache.GetOrCreate("CustomEggsCache", entry => {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                return _db.CustomEggs.ToList();
            });

            times.Set("Pre backups");
            await scoresTask;

            // Render from the stored backups for an instant page; only block on accounts that have no stored backup yet
            // (the view dereferences account.Backup directly). Fresh backups are refreshed out of band below.
            var accountsNeedingBackup = user.EggIncAccounts.Where(a => a.Backup is null).ToList();
            if(accountsNeedingBackup.Count > 0) {
                var fetched = await Task.WhenAll(accountsNeedingBackup.Select(a => AccountRefresh.RefreshBackupAsync(a, cachedContracts, _logger)));
                if(fetched.Any(b => b is not null)) {
                    user.UpdateAccounts();
                    await _db.SaveChangesAsync();
                }
            }

            // Refresh fresh backups in the background (own DI scope) so the next load is current without blocking this render.
            RefreshBackupsInBackground(user.DiscordId);

            times.Set("Post backups");


            Console.WriteLine(string.Join("\n", times.Finished().Select(y => $"{y.name}: {y.time.Humanize().ShortenTime()}")));
            return View("Index", new MyFarmsModel(user, Contracts, Demerits, Merits, /*RawBackups,*/ Snapshots, xrefs, coops, erItems, scoring, DbGuild, uncompletedPes, dbCustomEggs, isSelf, cachedContracts, seasonPEByEggIncId, missingSeasonalPEByEggIncId));
        }

        // Lazily renders the artifact-inventory image plus its hover-target manifest for one account. The
        // MyFarms inventory tab fetches this the first time it's opened, so the main page load never pays
        // for the image generation. Access is restricted to the account's owner or a staff member, matching
        // who is allowed to view the farms in the first place.
        [HttpGet]
        public async Task<IActionResult> InventoryOverlay(string eid) {
            if(string.IsNullOrWhiteSpace(eid)) return BadRequest(new { error = "Missing account id." });

            var loginuser = await _userManager.GetUserAsync(User);
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var loginUserId = ulong.Parse(logins.First().ProviderKey);

            var owner = await _db.DBUsers.FirstOrDefaultAsync(x => x.EIDs.Contains(eid));
            if(owner is null) return NotFound(new { error = "Account not found." });

            var isStaff = User.IsInRole("Admin") || User.IsInRole("GuildAdmin") || User.IsInRole("GuildLesserAdmin") || User.IsInRole("GuildReadOnlyAdmin");
            if(owner.DiscordId != loginUserId && !isStaff) return Forbid();

            var account = owner.EggIncAccounts.FirstOrDefault(a => a.Id == eid);
            if(account is null) return NotFound(new { error = "Account not found." });

            var render = _artifactRenderer.RenderInventory(account);
            if(!render.Ok) return Json(new { error = render.Error });

            return Json(new { imageB64 = Convert.ToBase64String(render.Jpeg), manifest = render.Manifest });
        }

        private async Task GetScores(DBUser user, List<(string EggIncId, MyContracts MyContracts)> scoring) {
            // MyContracts scores per account (cached 1h, network-only on miss). Never touches _db, so it is safe
            // to run concurrently with the controller's DB queries.
            var results = await Task.WhenAll(user.EggIncAccounts.Select(async account => {
                var scores = await _cache.GetOrCreateAsync($"{account.Id}-MyContracts", async entry => {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    return await EggIncApi.Post<MyContracts, BasicRequestInfo>(new BasicRequestInfo(), account.Id);
                });
                return (account.Id, scores);
            }));
            scoring.AddRange(results);
        }

        // Refreshes each account's backup from the Egg Inc API in its own DI scope and persists it, so the next
        // page load renders current data without this request blocking on the network / backup processing.
        private void RefreshBackupsInBackground(ulong discordId) {
            _ = Task.Run(async () => {
                try {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == discordId);
                    if(user is null) return;
                    var cachedContracts = await db.CachedEiContractsAsync();
                    // Backups are network-only + per-account in memory, so fetch them concurrently.
                    await Task.WhenAll(user.EggIncAccounts.Select(account => AccountRefresh.RefreshBackupAsync(account, cachedContracts, _logger)));
                    // Extras stage DB writes, so run them sequentially against the single context.
                    foreach(var account in user.EggIncAccounts)
                        await AccountRefresh.ApplyExtrasAsync(user, account, db, _logger);
                    user.UpdateAccounts();
                    await db.SaveChangesAsync();
                } catch(Exception e) {
                    _logger.LogError(e, "Background backup refresh failed for {discordId}", discordId);
                }
            });
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
            bool IsSelf,
            FrozenSet<Ei.Contract> CachedContracts,
            Dictionary<string, (int Earned, int Max)> SeasonPEByEggIncId,
            Dictionary<string, List<MissingSeasonalPe>> MissingSeasonalPEByEggIncId
        );

        public record MissingSeasonalPe(string SeasonName, double CurrentCxp, double GoalCxp, int PeAmount, DateTimeOffset StartTime);

        public async Task<IActionResult> EarningsBoostCalculator() {
            var loginuser = (await _userManager.GetUserAsync(User));

            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            //Get fresh backups concurrently (cachedContracts resolved up front - _db is not thread-safe).
            var cachedContracts = await _db.CachedEiContractsAsync();
            var freshBackups = await Task.WhenAll(user.EggIncAccounts.Select(async account => {
                var (backup, _) = await EggIncApi.GetBackupAsync(account.Id, cachedContracts);
                return (account, backup);
            }));
            foreach(var (account, backup) in freshBackups) {
                if(backup?.Farms is not null && backup.LastBackupTime > account.Backup.LastBackupTime) {
                    account.Backup = backup;
                }
            }

            var contractIDs = user.EggIncAccounts.SelectMany(b => b.Backup.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId)).ToList();
            ViewBag.Contracts = await _db.Contracts.AsQueryable().Where(x => contractIDs.Contains(x.ID)).ToListAsync();

            var boostEvent = await _db.Events.AsQueryable().Where(x => x.Type == "earnings-boost" && !x.Ended && x.Ends > DateTimeOffset.UtcNow).FirstOrDefaultAsync();

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
                existing.SubmittedAt = DateTimeOffset.UtcNow;
            }
            await _db.SaveChangesAsync();
            return Ok();
        }

        public record SaveContractSettingModel {
            public int AccountIndex { get; set; }
            public string Field { get; set; }
            public string Value { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SaveContractSetting([FromBody] SaveContractSettingModel m) {
            var loginuser = await _userManager.GetUserAsync(User);
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var dbuser = await _db.DBUsers.FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            if(m.AccountIndex < 0 || m.AccountIndex >= dbuser.EggIncAccounts.Count) return BadRequest();

            var account = dbuser.EggIncAccounts[m.AccountIndex];
            var s = account.Assignment ??= new Common.Contracts.Assignment.AssignmentSettings();
            var result = Common.Contracts.Assignment.ContractSettingField.Apply(s, m.Field, m.Value);
            if(result.Status != Common.Contracts.Assignment.ContractSettingApplyStatus.Ok) return BadRequest(result.Status.ToString());

            // Anti-dodge: the seasonal CS goal can never be below the grade floor (same as the Discord
            // path). The per-season PE-CS floor is applied at assignment, not here. Echo the effective
            // value back so the client can reflect any clamp.
            double? effectiveCsGoal = null;
            if(m.Field == "seasonalCsGoal") {
                s.Seasonal.CsGoal = s.Seasonal.EffectiveCsGoal(account.GetGrade());
                effectiveCsGoal = s.Seasonal.CsGoal;
            }

            dbuser.UpdateAccounts();
            await _db.SaveChangesAsync();
            return Ok(new { effectiveCsGoal });
        }

        public record TestAssignmentModel {
            public int AccountIndex { get; set; }
            public string ContractId { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> TestAssignment([FromBody] TestAssignmentModel m) {
            var loginuser = await _userManager.GetUserAsync(User);
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var dbuser = await _db.DBUsers.FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            if(m.AccountIndex < 0 || m.AccountIndex >= dbuser.EggIncAccounts.Count) return BadRequest();
            var account = dbuser.EggIncAccounts[m.AccountIndex];

            var contract = await _db.Contracts.FirstOrDefaultAsync(x => x.ID == m.ContractId);
            if(contract is null) return NotFound();

            var (season, seasonProgresses) = await Common.Contracts.OrganizeCoops.LoadContractSeasonData(_db, contract, new List<DBUser> { dbuser });

            var latest = await _db.UserCsHistoryEntries
                .Where(h => h.EggIncId == account.Id && h.ContractIdentifier == contract.ID)
                .OrderByDescending(h => h.Created)
                .FirstOrDefaultAsync();

            var contractFacts = Common.Contracts.Assignment.ContractFactsBuilder.Build(contract, season);
            var accountFacts = Common.Contracts.Assignment.AccountFactsBuilder.Build(dbuser, account, contract, new List<Coop>(), latest, season, seasonProgresses);

            var dbGuild = _db.CachedGuilds.FirstOrDefault(g => g.Id == dbuser.GuildId);
            var decision = Common.Contracts.Assignment.AssignmentEvaluator.Evaluate(accountFacts, contractFacts, account.Assignment, dbGuild?.RuleOverrides, dbGuild?.DisableBG ?? false, verbose: true);

            return Ok(new {
                assigned = decision.Assigned,
                diagnostics = new {
                    isSeasonal = contractFacts.IsSeasonal,
                    isLegacy = contractFacts.IsLegacy,
                    isUltra = contractFacts.IsUltra,
                    isColleggtible = contractFacts.IsColleggtible,
                    missingSeasonalPe = accountFacts.MissingSeasonalPe,
                    missingColleggtible = accountFacts.MissingColleggtible,
                    previouslyCompleted = accountFacts.PreviouslyCompleted,
                    previousScore = accountFacts.PreviousScoreOnThisContract,
                    filtersDisabled = dbGuild?.DisableBG ?? false
                },
                results = decision.Results.Select(r => new {
                    rule = r.Rule.ToString(),
                    tier = r.Tier.ToString(),
                    outcome = r.Outcome.ToString(),
                    reason = r.Reason
                })
            });
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
            return RedirectToLocalReferer();
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
            return RedirectToLocalReferer();
        }

        // Redirect back to the Referer only when it points at this same host, to avoid an open
        // redirect from a forged Referer header. Falls back to the site root.
        private IActionResult RedirectToLocalReferer() {
            var referer = Request.Headers["Referer"].ToString();
            if(Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == Request.Host.Host) {
                return Redirect(uri.PathAndQuery);
            }
            return Redirect("~/");
        }

        public async Task<IActionResult> SendTestDM([FromQuery] string target) {
            // Target is always the authenticated user - never trust a caller-supplied id, or anyone
            // could spam an arbitrary Discord user / channel-ping through the bot.
            var loginUser = await _userManager.GetUserAsync(User);
            var logins = loginUser is null ? null : await _userManager.GetLoginsAsync(loginUser);
            if(!ulong.TryParse(logins?.FirstOrDefault()?.ProviderKey, out var discorduserid))
                return BadRequest();
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
            var coop = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Include(x => x.Contract).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == CoopId);
            var customEggs = await _db.GetCustomEggsAsync();
            return View((coop, customEggs));
        }
    }
}