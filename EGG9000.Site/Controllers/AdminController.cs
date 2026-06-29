using Discord;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Consumers;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Services;
using EGG9000.Site.Services;

using Ei;

using MassTransit;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static Ei.Contract.Types;

using Contract = EGG9000.Common.Database.Entities.Contract;
using EventCustomization = EGG9000.Common.Database.Entities.EventCustomization;

namespace EGG9000.Site.Controllers {
    [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin,GuildReadOnlyAdmin")]
    public class AdminController(UserManager<ApplicationUser> userManager, DiscordSocketClient discord,
        ApplicationDbContext db, IMemoryCache cache, ILogger<AdminController> logger, IConfiguration configuration, IPublishEndpoint publishEndpoint) : Controller {

        private readonly ApplicationDbContext _db = db;
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly DiscordSocketClient _discord = discord;
        private readonly IMemoryCache _cache = cache;
        private readonly ILogger<AdminController> _logger = logger;
        private readonly IConfiguration _configuration = configuration;
        private readonly IPublishEndpoint _publishEndpoint = publishEndpoint;

        public class PrestigeGain {
            public UserSnapShot SnapShot { get; set; }
            public DBUser User { get; set; }
            public ulong Gain { get; set; }
        }


        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<ActionResult> LatestDemerits([FromQuery] int count = 100) {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guildId);
            var demerits = await _db.Demerit.AsQueryable().Include(d => d.User).Where(d => d.User.GuildId == guildId).OrderByDescending(d => d.When).ToListAsync();
            var limited = false;
            if(demerits.Count > count) {
                limited = true;
                demerits = demerits.Take(count).ToList();
            }

            return View((demerits, dbguild.Name, count, limited));
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> RemoveDemerit([FromQuery] Guid id) {
            var demerit = _db.Demerit.FirstOrDefault(x => x.Id == id);
            _db.Remove(demerit);
            await _db.SaveChangesAsync();
            return RedirectToLocalReferer();
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RestartBot() {
            await _publishEndpoint.Publish(new RestartMessage());
            return Content("Bot proccess ended.");
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> LookForLargeJump() {
            var snapshots = await _db.UserSnapShots.ToListAsync();

            var sgroups = snapshots.GroupBy(x => x.UserId);

            var gains = new List<PrestigeGain>();
            var users = new List<DBUser>();
            foreach(var sagroup in sgroups) {
                foreach(var sgroup in sagroup.GroupBy(x => x.EggIncID)) {
                    var osnaps = sgroup.OrderBy(x => x.Prestiges);
                    ulong previousPrestiges = 0;
                    foreach(var osnap in osnaps) {
                        if(previousPrestiges > 0 && osnap.Prestiges > previousPrestiges + 100) {
                            var user = users.FirstOrDefault(x => x.Id == sagroup.Key);
                            if(user == null) {
                                user = await _db.DBUsers.FirstOrDefaultAsync(x => x.Id == sagroup.Key && x.GuildId == 656455567858073601);
                                if(user != null) {
                                    users.Add(user);
                                }
                            }
                            if(user != null) {
                                gains.Add(new PrestigeGain { SnapShot = osnap, User = user, Gain = osnap.Prestiges - previousPrestiges });
                            }
                        }
                        previousPrestiges = osnap.Prestiges;
                    }
                }
            }

            foreach(var gain in gains.OrderByDescending(x => x.Gain).Take(25)) {
                Console.WriteLine($"{gain.User.DiscordUsername} on {gain.SnapShot.Date.ToShortDateString()} jumped by {gain.Gain} prestiges, {gain.SnapShot.EggIncID}");
            }

            return Content("Success");
        }

        public async Task<IActionResult> GetGraphs() {
            if(NewCoopChecker.WaitingOnCoops) {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            _db.Database.SetCommandTimeout(360);
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            Dictionary<DateTimeOffset, int[]> days;
            var adminDaysCacheKey = $"AdminDaysV2{guildId}";
            if(!_cache.TryGetValue(adminDaysCacheKey, out days)) {

                days = [];
                var coops = (await _db.Coops.AsQueryable().Select(x => new { x.Created, Finished = x.CoopCompleted ?? x.CoopEnds, x.GuildId }).ToListAsync())
                    .Where(x => x.Created != DateTimeOffset.MinValue).ToList();

                var userDates = await _db.DBUsers.Where(x => x.UserCoopXrefs.Any(y => y.JoinedCoop))
                    .Select(x => new {
                        Start = x.UserCoopXrefs.Where(y => y.JoinedCoop).OrderBy(y => y.CreatedOn).First().CreatedOn,
                        End = x.UserCoopXrefs.Where(y => y.JoinedCoop).OrderByDescending(y => y.CreatedOn).First().CreatedOn
                    }).ToListAsync();

                var guildUserDates = (await _db.UserCoopXrefs
                    .Where(y => y.JoinedCoop && y.Coop.GuildId == guildId)
                    .Select(y => new { y.UserId, y.CreatedOn })
                    .ToListAsync())
                    .GroupBy(y => y.UserId)
                    .Select(g => new { Start = g.Min(y => y.CreatedOn), End = g.Max(y => y.CreatedOn) })
                    .ToList();

                for(var start = coops.OrderBy(x => x.Created).First().Created.Date; start <= DateTimeOffset.UtcNow; start = start.AddDays(1)) {
                    var count = coops.Count(c => c.Created.Date <= start && (c.Finished?.Date ?? c.Created.AddDays(4).Date) >= start);
                    var accountsCount = userDates.Count(x => x.Start < start && x.End > start.AddDays(-14));
                    var guildCount = coops.Count(c => c.GuildId == guildId && c.Created.Date <= start && (c.Finished?.Date ?? c.Created.AddDays(4).Date) >= start);
                    var guildAccountsCount = guildUserDates.Count(x => x.Start < start && x.End > start.AddDays(-14));
                    days.Add(start, [count, accountsCount, guildCount, guildAccountsCount]);
                }
                _cache.Set(adminDaysCacheKey, days, TimeSpan.FromHours(1));
            }


            return Json(new {
                days = days.Select(x => new object[] { x.Key.ToUnixTimeMilliseconds(), x.Value[0] }),
                days2 = days.Select(x => new object[] { x.Key.ToUnixTimeMilliseconds(), x.Value[1] }),
                guildDays = days.Select(x => new object[] { x.Key.ToUnixTimeMilliseconds(), x.Value[2] }),
                guildDays2 = days.Select(x => new object[] { x.Key.ToUnixTimeMilliseconds(), x.Value[3] })
            });
        }

        public async Task<IActionResult> Index() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            var guildContractsToScore = await _db.GuildContracts.Include(x => x.Contract).AsQueryable()
                .Where(x => x.Contract.MaxUsers > 1 && x.GuildID == 656455567858073601 && x.Created > DateTimeOffset.UtcNow.AddMonths(-3) && !x.HasScores)
                .OrderBy(x => x.Created).ToListAsync();
            var contractsToScore = guildContractsToScore.GroupBy(x => x.ContractID).Where(x => x.All(y => y.Contract.Details.GradeSpecs.Any(gs => gs.LengthSeconds > TimeSpan.FromDays(1).TotalSeconds) && y.Created < DateTimeOffset.UtcNow - y.Contract.ContractTime - TimeSpan.FromDays(3))).Select(x => x.First().Contract).ToList();

            return View(new IndexViewModel {
                Contracts = await _db.Contracts.AsQueryable().OrderByDescending(x => x.Created).Take(10).ToListAsync(),
                Guilds = _discord.Guilds.Where(x => x.Id == guildId || guild.OverflowServers.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new GuildDetails {
                    Name = x.Name,
                    ThreadCount = x.GetInUseThreadCount(),
                    ActiveCoops = x.ThreadChannels.Where(t => !t.IsArchived && Regex.IsMatch(t.ParentChannel?.Name, @"(-aaa|-aa|-a|-b|-c)$")).Count(c => !c.Name.Contains("🏁") && !c.Name.Contains("🚩")),
                    FinishedCoops = x.ThreadChannels.Where(t => !t.IsArchived && Regex.IsMatch(t.ParentChannel?.Name, @"(-aaa|-aa|-a|-b|-c)$")).Count(c => c.Name.Contains("🏁") || c.Name.Contains("🚩")),
                }).ToList(),
                Guild = guild,
                ContractsToScore = contractsToScore,
                CoopsWithoutThreads = await _db.Coops.CountAsync(x => x.ThreadID == 0 && (x.Status == CoopStatusEnum.WaitingOnThread || x.Status == CoopStatusEnum.WaitingOnCreation) && !x.DeletedChannel && x.CoopEnds > DateTimeOffset.UtcNow)
            });
        }

        public class IndexViewModel {
            public List<Contract> Contracts { get; set; }
            public List<GuildDetails> Guilds { get; set; }
            public Dictionary<DateTimeOffset, int[]> Days { get; set; }
            public List<Contract> ContractsToScore { get; set; }
            public Guild Guild { get; set; }
            public int CoopsWithoutThreads { get; set; }
        }

        public class GuildDetails {
            public string Name { get; set; }
            public int ThreadCount { get; set; }
            public int ActiveCoops { get; set; }
            public int FinishedCoops { get; set; }
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> EventCustomization() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            var dbCustomizations = await _db.EventCustomizations.ToListAsync();
            var eventTypes = dbCustomizations.Select(x => x.Type).ToList();

            var periodicalsResponse = await EggIncApi.GetPeriodicalsAsync();
            var periodicalsTypes = periodicalsResponse.Events.Events.Where(x => !eventTypes.Contains(x.Type)).Select(x => x.Type).ToList();
            eventTypes.AddRange(periodicalsTypes);

            var guildTypes = (await _db.GetCustomizationsAsync(guild)).Where(x => !eventTypes.Contains(x.Type)).Select(x => x.Type).ToList();
            eventTypes.AddRange(guildTypes);

            var eventCustomizations = new List<EventCustomization>();
            foreach(var type in eventTypes) {
                var guildEventCustomization = await _db.GetCustomizationAsync(guild, type);
                eventCustomizations.Add(guildEventCustomization ?? new() { Type = type });
            }

            return View(eventCustomizations.OrderByDescending(x => x.Priority).ToList());
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> SaveEventCustomization([FromBody] EventCustomization eventCustomization) {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            var eventCustomizationToSave = guild.EventCustomizations.FirstOrDefault(ec => ec.Type == eventCustomization.Type);

            var tempNotifs = JsonConvert.DeserializeObject<EventCustomizationSettings>(eventCustomization._settings);
            tempNotifs.Notifications.ForEach(n => n.GuildID = guild.DiscordSeverId);
            eventCustomization._settings = JsonConvert.SerializeObject(tempNotifs);

            if(eventCustomizationToSave is null) {
                guild.EventCustomizations = [
                    .. guild.EventCustomizations,
                    eventCustomization
                ];
            } else {
                var cloneList = new List<EventCustomization>(guild.EventCustomizations) {
                    [guild.EventCustomizations.IndexOf(eventCustomizationToSave)] = eventCustomization
                };
                guild.EventCustomizations = cloneList;
            }

            await _db.SaveChangesAsyncRetry(2);
            var guildKey = _db.InvalidateEventCustomizations(guild);
            await _publishEndpoint.Publish(new ExpireCacheMessage(guildKey));
            return Content("Success");
        }

        public class FAQCustomizationModel() {
            public List<FAQTopic> PalaceFAQTopics { get; set; }
            public List<FAQTopic> GuildFAQTopics { get; set; }
            public ulong PalaceGuildId { get; set; }
            public ulong GuildId { get; set; }
            public string GuildName { get; set; }
            public string UserDiscordUsername { get; set; }
            public ulong UserDiscordId { get; set; }
            public int KeywordMaxLength { get; set; }
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> FAQCustomization() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            var palaceGuild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == KnownGuilds.Palace);

            var palaceFaqs = await _db.FAQTopics.Where(x => x.GuildId == palaceGuild.Id).ToListAsync();
            var guildFaqs = await _db.FAQTopics.Where(x => x.GuildId == guild.Id).ToListAsync(); ;
            var allFaqs = await _db.FAQTopics.ToListAsync();
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            var model = new FAQCustomizationModel() {
                PalaceFAQTopics = palaceFaqs,
                GuildFAQTopics = guildFaqs,
                PalaceGuildId = KnownGuilds.Palace,
                GuildId = guildId,
                GuildName = guild.Name,
                UserDiscordUsername = user.DiscordUsername,
                UserDiscordId = user.DiscordId,
                KeywordMaxLength = FAQHelper.MAX_KEYWORD_LENGTH
            };

            return View(model);
        }

        [Authorize]
        public IActionResult SaveFAQ() {
            return View();
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> SaveFAQTopic([FromBody] FAQTopic faqTopic) {

            var loginuser = await _userManager.GetUserAsync(User);
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);



            var wasFound = false;
            FAQTopic topicToSave = null;
            if(faqTopic.InternalId != "") {
                topicToSave = _db.FAQTopics.FirstOrDefault(f => f.InternalId == faqTopic.InternalId);
                wasFound = topicToSave != null;
            } else {
                topicToSave = faqTopic;
                topicToSave.InternalId = $"{DateTime.Now}_{faqTopic.CreatedById}_{faqTopic.Name}".Replace(" ", "_");
            }

            if(!wasFound) {
                topicToSave.GuildId = guildId;
                topicToSave.CreatedById = ulong.Parse(logins.First().ProviderKey);
                _db.FAQTopics.Add(faqTopic);
            } else {
                faqTopic.GuildId = ulong.Parse(faqTopic.GuildIdString);
                faqTopic.CreatedById = ulong.Parse(faqTopic.CreatedByIdString);
                _db.Remove(topicToSave);
                _db.FAQTopics.Add(faqTopic);
            }

            var guildKey = _db.InvalidateFAQTopics(guild);
            await _publishEndpoint.Publish(new ExpireCacheMessage(guildKey));
            await _db.SaveChangesAsync();
            return Content("Success");
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> DeleteFAQTopic([FromBody] FAQTopic faqTopic) {

            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            var wasFound = false;
            FAQTopic topicToDelete = null;
            if(faqTopic.InternalId != "") {
                topicToDelete = _db.FAQTopics.FirstOrDefault(f => f.InternalId == faqTopic.InternalId);
                wasFound = topicToDelete != null;
            } else return Content("Failure");

            if(!wasFound) return Content("Failure");
            else {
                _db.FAQTopics.Remove(topicToDelete);
            }

            var guildKey = _db.InvalidateFAQTopics(guild);
            await _publishEndpoint.Publish(new ExpireCacheMessage(guildKey));
            await _db.SaveChangesAsync();
            return Content("Success");
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UserPermissions() {
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));


            return View(new UserPermissionsModel {
                Users = await _db.Users.AsQueryable().ToListAsync(),
                Logins = await _db.UserLogins.AsQueryable().ToListAsync(),
                UserRoles = await _db.UserRoles.AsQueryable().ToListAsync(),
                Roles = await _db.Roles.AsQueryable().ToListAsync(),
                DbUsers = (await _db.DBUsers.AsQueryable().Select(x => new {
                    x.DiscordUsername,
                    x.DiscordId
                }).ToListAsync()).Select(x => new DBUser { DiscordId = x.DiscordId, DiscordUsername = x.DiscordUsername }).ToList()
            });
        }

        public class UserPermissionsModel {
            public List<ApplicationUser> Users { get; set; }
            public List<IdentityUserLogin<string>> Logins { get; set; }
            public List<IdentityUserRole<string>> UserRoles { get; set; }
            public List<IdentityRole> Roles { get; set; }
            public List<DBUser> DbUsers { get; set; }
        }

        public async Task<IActionResult> Contract([FromQuery] string contractid) {
            ViewBag.ContractID = contractid;
            ViewBag.Guilds = await _db.Guilds.AsQueryable().ToListAsync();
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));



            var rawusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == user.GuildId).Select(x => new {
                x.DiscordId,
                x.DiscordUsername,
                x.GuildId,
                x.Id,
                x._CustomBackups,
                x._eggIncIds,
                x.Registered
            }).ToListAsync();
            var dbusers = rawusers.Select(x => new DBUser { DiscordId = x.DiscordId, DiscordUsername = x.DiscordUsername, GuildId = x.GuildId, Id = x.Id, _CustomBackups = x._CustomBackups, _eggIncIds = x._eggIncIds, Registered = x.Registered });

            return View(dbusers.Where(x => x.EggIncAccounts.Any(y => y.Backup != null)).ToList());
        }

        public async Task<IActionResult> ContractScores([FromQuery] string contractid) {
            ViewBag.ContractID = contractid;
            ViewBag.Guilds = await _db.Guilds.AsQueryable().ToListAsync();
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));


            var coops = await _db.Coops.AsQueryable().Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.GuildId == user.GuildId && x.ContractID == contractid).ToListAsync();
            var contract = await _db.Contracts.FirstAsync(x => x.ID == contractid);

            var scores = ContractScoring.GetContractScores(coops, contract, _logger);


            return View(scores);
        }

        public async Task<IActionResult> Slackers() {
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.FirstAsync(x => x.Id == guildId);
            var scoreThreshold = guild.MinimumRunningScore;
            ViewBag.MinimumRunningScore = scoreThreshold;

            var slackers = await _db.DBUsers.AsQueryable().Include(x => x.UserCoopXrefs).Where(x => x.GuildId == user.GuildId && x.UserCoopXrefs.Any(y => y.RunningScore < scoreThreshold)).Select(x => new Slacker {
                DiscordUsername = x.DiscordUsername,
                UserCoopXrefs = x.UserCoopXrefs.Select(y => new SlackerXref {
                    Score = y.Score,
                    ContractID = y.Coop.ContractID,
                    RunningScore = y.RunningScore,
                    Date = y.Coop.CoopCompleted ?? y.Coop.CoopEnds ?? y.CreatedOn
                }),
                Id = x.Id,
            }).ToListAsync();

            slackers = slackers.Where(x => x.UserCoopXrefs.Any(y => y.RunningScore < scoreThreshold && y.Date > DateTimeOffset.UtcNow.AddMonths(-4))).ToList();


            var ids = slackers.Select(x => x.Id).ToList();
            var users = await _db.DBUsers.Where(x => ids.Contains(x.Id)).ToListAsync();
            foreach(var item in slackers) {
                var account = users.First(x => x.Id == item.Id);
                item.AccountCount = account.EggIncAccounts.Count;
                item.Standard = account.EggIncAccounts.Any(y => y.Backup.PermitLevel == 0);
            }

            ViewBag.Contracts = await _db.Contracts.AsQueryable().Where(x => x.Created > DateTimeOffset.UtcNow.AddMonths(-6)).ToListAsync();

            return View(slackers);
        }

        public class Slacker {
            public string DiscordUsername { get; set; }
            public bool Standard { get; set; }
            public int AccountCount { get; set; }
            public IEnumerable<SlackerXref> UserCoopXrefs { get; set; }
            public Guid Id { get; set; }
        }

        public class SlackerXref {
            public float? Score { get; set; }
            public string ContractID { get; set; }
            public float? RunningScore { get; set; }
            public DateTimeOffset Date { get; set; }
        }


        public async Task<IActionResult> DeleteOutsideCoopMessage() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var coops = await _db.Coops.AsQueryable().Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == guildId && !x.DeletedChannel && !x.ThreadArchived).ToListAsync();

            var coopsToFix = coops.Where(x => x.UserCoopsXrefs.Any(y => y.OutsideCoop));

            foreach(var coop in coopsToFix) {
                var channel = coop.ThreadID != 0 ? _discord.Guilds.First(x => x.Id == coop.OverflowGuildId).GetTextChannel(coop.ThreadID) :
                    _discord.Guilds.First(x => x.Id == coop.OverflowGuildId).GetTextChannel(coop.DiscordChannelId);
                var messages = await channel.GetMessagesAsync().FlattenAsync();
                foreach(var message in messages.Where(x => x.Content.Contains("has joined another co-op named . Please use the command"))) {
                    Console.WriteLine($"Deleting message from {coop.Name}");
                    await message.DeleteAsync();
                }

            }

            return Content("Success");
        }
        public async Task<IActionResult> CalculateScore([FromQuery] string contractid) {
            _db.Database.SetCommandTimeout(360);
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guildContracts = await _db.GuildContracts.Include(x => x.Contract).AsQueryable().Where(x => x.ContractID == contractid && x.GuildID == guildId).ToListAsync();
            var contractCoops = await _db.Coops.AsQueryable().Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User)
                .Where(x =>
                    x.ContractID == contractid &&
                    x.GuildId == guildId &&
                    x.Created > DateTimeOffset.UtcNow.AddMonths(-6)
                ).ToListAsync();
            Console.WriteLine($"Processing {contractid}");
            var contract = await _db.Contracts.FirstAsync(x => x.ID == contractid);
            var scores = ContractScoring.GetContractScores(contractCoops, contract, _logger);
            var userXrefs = await _db.UserCoopXrefs.Where(x => x.Score != null && x.Coop.ContractID != contractid && x.CreatedOn < contract.GoodUntil)
                .GroupBy(x => x.UserId).Select(x => new { Key = x.Key, Last3Score = x.OrderByDescending(y => y.CreatedOn).Take(3) }).ToListAsync();
            foreach(var score in scores) {
                score.xref.Score = score.Score;
                score.xref.SoulPower = score.SoulPower;
                var xrefs = userXrefs.FirstOrDefault(x => x.Key == score.UserId)?.Last3Score.ToList() ?? [];
                xrefs.Add(score.xref);
                if(xrefs.Count == 4) {
                    var firstXref = xrefs.First();
                    score.xref.RunningScore = xrefs.Average(x => x.Score);
                    var eggIncAccount = firstXref.User?.EggIncAccounts?.FirstOrDefault(a => a.Id == firstXref.EggIncId);
                    if(eggIncAccount != null) {
                        eggIncAccount.LatestRunningScore = xrefs.Average(x => x.Score) ?? 0;
                        firstXref.User.UpdateAccounts();
                    }
                }
            }
            guildContracts.Where(x => x.ContractID == contractid).ToList().ForEach(x => x.HasScores = true);
            await _db.SaveChangesAsync();

            var users = await _db.DBUsers.Where(x => x.GuildId == guildId).Select(x => new {
                x.Id,
                x.DiscordId,
                x.DiscordUsername,
                x._eggIncIds,
                x.TempDisabled
            }).ToListAsync();

            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guildId);
            var scoreThreshold = dbguild.MinimumRunningScore;


            var xrefsBelowThreshold = scores.Where(x => x.xref.RunningScore < scoreThreshold).Select(y => {
                var user = users.FirstOrDefault(u => u.Id == y.UserId);
                return (user is null) ? null : new ScoreUser {
                    DiscordId = user.DiscordId,
                    DiscordUsername = user.DiscordUsername,
                    RunningScore = y.xref.RunningScore.Value,
                    Grade = (Ei.Contract.Types.PlayerGrade)y.League,
                    EggIncId = y.xref?.EggIncId ?? "",
                    Disabled = user.TempDisabled
                };
            });

            var guild = _discord.GetGuild(guildId);
            var beastModeRole = guild.GetRole(938563459812049008);

            var allTopXrefs = scores.Select(y => {
                var user = users.FirstOrDefault(u => u.Id == y.UserId);
                return (user is null) ? null : new ScoreUser {
                    DiscordId = user.DiscordId,
                    DiscordUsername = user.DiscordUsername,
                    Score = y.Score,
                    DiscordUser = guild.GetUser(user.DiscordId),
                    Grade = (Ei.Contract.Types.PlayerGrade)y.League,
                    EggIncId = y.xref?.EggIncId ?? ""
                };
            });

            var topXrefs = allTopXrefs.Where(x => x != null).OrderByDescending(x => x.Score).Take(10).ToList();
            var topEachGrade = allTopXrefs.Where(x => x != null).GroupBy(x => x.Grade).Where(g => g.Key <= Ei.Contract.Types.PlayerGrade.GradeA).Select(g => g.OrderByDescending(u => u.Score).First()).ToList();
            var usersForRole = topXrefs.Union(topEachGrade);

            foreach(var topxref in usersForRole) {
                topxref.DiscordUser ??= await _discord.Rest.GetGuildUserAsync(guildId, topxref.DiscordId);
                var tempRole = await _db.TemporaryRoles.FirstOrDefaultAsync(x => x.RoleId == beastModeRole.Id && topxref.DiscordId == x.UserId && x.Expires > DateTimeOffset.UtcNow);
                if(tempRole == null) {
                    tempRole = new TemporaryRole { RoleId = beastModeRole.Id, Created = DateTimeOffset.UtcNow, UserId = topxref.DiscordId, GuildId = guildId };
                    _db.Add(tempRole);
                    try {
                        await topxref.DiscordUser.AddRoleAsync(beastModeRole);
                        Console.WriteLine($"Role added to {topxref.DiscordUser.Nickname}");
                        await Task.Delay(600);
                    } finally { }
                }
                tempRole.Reason = $"{beastModeRole.Name} awarded for {guildContracts.First().Contract.Name}";
                tempRole.Expires = DateTimeOffset.UtcNow.AddDays(7);
            }

            await _db.SaveChangesAsync();

            await guild.GetTextChannel(656455568353132546)
                .SendMessageAsync(
                    text: $"Added the role {beastModeRole.Emoji} {beastModeRole.Name} to the following users until <t:{DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()}:f> " +
                        $"for the contract {guildContracts.First().Contract.Name} \n{string.Join("\n", topXrefs.Select(x => $"{Math.Round(x.Score)} <@{x.DiscordId}>"))}" +
                        $"{(topEachGrade.Count == 0 ? "" :
                            $"\n\nTop users in Grades C, B, and A also received {beastModeRole.Emoji} {beastModeRole.Name} until <t:{DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds()}:f>:\n" +
                            $"{string.Join("\n", topEachGrade.Select(x => $"{PlayerGradeDetails.GetEmoji(x.Grade)}: {Math.Round(x.Score)} <@{x.DiscordId}>"))}")}",
                    components: new ComponentBuilder().WithButton("What is this?", "WhatIsRSC", ButtonStyle.Primary).Build()
                );

            return View(new ScoreResult {
                UsersBelowThreshold = [.. xrefsBelowThreshold.Where(x => x != null).OrderBy(x => x.DiscordUsername)],
                TopScore = [.. topXrefs]
            });
        }
        public async Task<IActionResult> ReCalculateRunningScore() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);

            var coops = await _db.Coops.AsQueryable().Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == guildId && x.Created > DateTimeOffset.UtcNow.AddMonths(-6)).ToListAsync();

            var userXrefs = coops.SelectMany(x => x.UserCoopsXrefs).Where(x => x.JoinedCoop).GroupBy(x => x.UserId);

            foreach(var userXref in userXrefs) {
                var xrefs = userXref.OrderByDescending(x => x.CreatedOn).ToList();
                foreach(var xref in xrefs) {
                    var lastFourXrefs = xrefs.Where(x => x.CreatedOn <= xref.CreatedOn && x.Score.HasValue).Take(4).ToList();
                    if(lastFourXrefs.Count == 4 && xref.Score.HasValue) {
                        xref.RunningScore = lastFourXrefs.Average(x => x.Score);
                    } else {
                        xref.RunningScore = null;
                    }
                }
            }
            await _db.SaveChangesAsync();
            return Content("Success");
        }

        public class ScoreResult {
            public List<ScoreUser> UsersBelowThreshold { get; set; }
            public List<ScoreUser> TopScore { get; set; }
        }

        public class ScoreUser {
            public ulong DiscordId { get; set; }
            public string DiscordUsername { get; set; }
            public float RunningScore { get; set; }
            public float Score { get; set; }
            public IGuildUser DiscordUser { get; set; }
            public Ei.Contract.Types.PlayerGrade Grade { get; set; }
            public string EggIncId { get; set; }
            public bool Disabled { get; set; }
        }

        public async Task<IActionResult> Sleepers() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var demeritExpires = DateTimeOffset.UtcNow.AddDays(-2);
            var sleepers = await _db.UserCoopXrefs.AsQueryable().Where(x => x.User.GuildId == guildId && !x.Coop.DeletedChannel && !x.Coop.ThreadArchived).Select(x => new SleeperDetail {
                DiscordName = x.User.DiscordUsername,
                CurrentSleep = x.HoursSleeping - (x.SiloTimeHours ?? 0),
                TotalCoopSleep = x.TotalHoursSleeping,
                CoopName = x.Coop.Name,
                ContractName = x.Coop.Contract.Name,
                DiscordChannelId = x.Coop.ThreadID != 0 ? x.Coop.ThreadID : x.Coop.DiscordChannelId,
                GuildId = guildId,
                Demerits = x.User.Demerits.Where(y => y.When > demeritExpires).ToList(),
                FreshEgg = x.User.Registered > DateTimeOffset.UtcNow.AddDays(-7)
            }).Where(x => x.CurrentSleep > 17 || x.TotalCoopSleep >= 24).ToListAsync();


            return View(sleepers);
        }

        public class SleeperDetail {
            public bool FreshEgg { get; set; }
            public List<Demerit> Demerits { get; set; }
            public string DiscordName { get; set; }
            public float CurrentSleep { get; set; }
            public float TotalCoopSleep { get; set; }
            public string CoopName { get; set; }
            public string ContractName { get; set; }
            public ulong DiscordChannelId { get; set; }
            public ulong GuildId { get; set; }

        }

        public async Task<IActionResult> Ghosts() {
            _db.Database.SetCommandTimeout(360);
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var dbguild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            var guild = _discord.Guilds.First(x => x.Id == guildId);
            await guild.DownloadUsersAsync();
            var needToJoinChannel = guild.TextChannels.FirstOrDefault(x => x.Id == 775558629671698442);
            var allMessages = needToJoinChannel is null ? [] : await needToJoinChannel.GetMessagesAsync(1000).FlattenAsync();
            var allMentions = allMessages.SelectMany(x => x.MentionedUserIds);



            var allGhosts = new List<Ghost>();
            foreach(var overflowGuildId in dbguild.OverflowServers) {
                var overflowGuild = _discord.Guilds.First(x => x.Id == overflowGuildId);
                await overflowGuild.DownloadUsersAsync();
                var oneWeekAgo = DateTimeOffset.UtcNow.AddDays(-7);
                var xrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => !x.Coop.DeletedChannel && !x.Coop.ThreadArchived && x.Coop.OverflowGuildId == overflowGuildId && !x.JoinedCoop).Select(x => new Ghost {
                    Coop = x.Coop.Name,
                    DiscordId = x.User.DiscordId,
                    CoopChannel = x.Coop.ThreadID != 0 ? x.Coop.ThreadID : x.Coop.DiscordChannelId,
                    UserName = x.User.DiscordUsername,
                    CoopId = x.CoopId,
                    UserId = x.UserId,
                    CoopFinished = x.Coop.Finished
                }).ToListAsync();

                var ghosts = xrefs.Where(x => !overflowGuild.Users.Any(y => y.Id == x.DiscordId)).ToList();
                ghosts.ForEach(x => {
                    x.ServerName = overflowGuild.Name;
                    x.Mentioned = allMentions.Any(y => y == x.DiscordId);
                    x.LastMention = allMessages.Where(z => z.MentionedUserIds.Any(y => y == x.DiscordId)).OrderByDescending(y => y.CreatedAt).FirstOrDefault()?.CreatedAt;
                    x.MissingFromMain = !guild.Users.Any(y => y.Id == x.DiscordId);
                });

                allGhosts.AddRange(ghosts);
            }


            foreach(var message in allMessages.Where(x => x.MentionedUserIds.Count == 1)) {
                if(!allGhosts.Any(g => g.DiscordId == message.MentionedUserIds.First())) {
                    var discorduser = guild.Users.FirstOrDefault(x => x.Id == message.MentionedUserIds.First());
                    if(discorduser is not null && !discorduser.Roles.Any(x => x.Id == KnownRoles.Overflow)) {
                        await message.DeleteAsync();
                    }
                }
            }
            return View(allGhosts);
        }

        public async Task<IActionResult> DeleteGhost([FromQuery] Guid UserId, [FromQuery] Guid CoopId) {
            var xref = await _db.UserCoopXrefs.Include(x => x.Coop).FirstAsync(x => x.UserId == UserId && x.CoopId == CoopId);
            if(!VerifyId(xref.Coop.GuildId)) {
                return NotFound();
            }
            _db.Remove(xref);
            await _db.SaveChangesAsync();
            return RedirectToAction("Ghosts");
        }

        public class Ghost {
            public string Coop { get; set; }
            public ulong DiscordId { get; set; }
            public ulong CoopChannel { get; set; }
            public string ServerName { get; set; }
            public string UserName { get; set; }
            public bool Mentioned { get; set; }
            public DateTimeOffset? LastMention { get; set; }
            public bool MissingFromMain { get; set; }
            public Guid CoopId { get; set; }
            public Guid UserId { get; set; }
            public bool CoopFinished { get; set; }
        }

        public async Task<IActionResult> Leechers([FromQuery] string contractid) {
            var coops = await _db.Coops.AsQueryable().Where(x => x.ContractID == contractid).ToListAsync();
            var leechers = coops.SelectMany(x => x.LastStatusUpdate.Contributors.Where(y => y.Leech).Select(y => y.UserId));
            ViewBag.Xrefs = await _db.UserCoopXrefs.Include(x => x.User).AsQueryable().Where(x => leechers.Contains(x.EggIncId) && x.Coop.ContractID == contractid).ToListAsync();
            return View(coops);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EditUsers() {
            var users = await _db.Users.AsQueryable().ToListAsync();
            var customNames = await _db.DBUsers.AsQueryable().Where(x => !string.IsNullOrWhiteSpace(x.CustomCoopName))
                .Select(x => new EditUserWithDetails {
                    CustomCoopName = x.CustomCoopName,
                    ExpireCustomCoopName = x.ExpireCustomCoopName,
                    DBUserId = x.Id,
                    DiscordId = x.DiscordId.ToString()
                }).ToListAsync();
            var userRoles = await _db.UserRoles.AsQueryable().ToListAsync();
            var roles = await _db.Roles.AsQueryable().ToListAsync();
            var userLogins = await _db.UserLogins.AsQueryable().ToListAsync();


            var editUserList = users.Select(x => {
                var DiscordId = userLogins.FirstOrDefault(y => y.UserId == x.Id)?.ProviderKey;
                var customName = customNames.FirstOrDefault(y => y.DiscordId == DiscordId);
                return new EditUserWithDetails {
                    ApplicationUser = x,
                    IdentityUserRoles = userRoles.Where(y => y.UserId == x.Id).ToList(),
                    DiscordId = DiscordId,
                    CustomCoopName = customName?.CustomCoopName,
                    ExpireCustomCoopName = customName?.ExpireCustomCoopName
                };
            });



            return View(new EditUserModel {
                Users = editUserList.ToList(),
                Roles = roles,
                DiscordGuilds = [.. _discord.Guilds],
                DbGuilds = await _db.Guilds.AsQueryable().ToListAsync()
            });
        }


        public async Task<IActionResult> AddGuildToDb(ulong id) {
            var discordGuild = _discord.GetGuild(id);
            var dbGuild = new Guild {
                Id = id,
                DiscordSeverId = id,
                Name = discordGuild.Name,
            };
            _db.Guilds.Add(dbGuild);
            await _db.SaveChangesAsync();
            return RedirectToAction("EditUsers");
        }

        public class EditUserModel {
            public List<EditUserWithDetails> Users { get; set; }
            public List<IdentityRole> Roles { get; set; }
            public List<SocketGuild> DiscordGuilds { get; set; }
            public List<Guild> DbGuilds { get; set; }
        }

        public class EditUserWithDetails {
            public string CustomCoopName { get; set; }
            public DateTimeOffset? ExpireCustomCoopName { get; set; }
            public Guid DBUserId { get; set; }
            public string DiscordId { get; set; }
            public ApplicationUser ApplicationUser { get; set; }
            public List<IdentityUserRole<string>> IdentityUserRoles { get; set; }
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetRole([FromForm] bool SetRole, [FromForm] string UserId, [FromForm] string RoleId) {
            var existingRole = await _db.UserRoles.AsQueryable().FirstOrDefaultAsync(x => x.RoleId == RoleId && x.UserId == UserId);
            if(SetRole) {
                if(existingRole == null) {
                    var newRole = new IdentityUserRole<string> { UserId = UserId, RoleId = RoleId };
                    _db.Add(newRole);
                    await _db.SaveChangesAsync();
                }
            } else {
                if(existingRole != null) {
                    _db.Remove(existingRole);
                    await _db.SaveChangesAsync();
                }
            }
            return Json(SetRole);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SetCustomName([FromForm] string CustomName, [FromForm] string DiscordID) {
            var user = await _db.DBUsers.FirstAsync(x => x.DiscordId.ToString() == DiscordID);
            user.CustomCoopName = CustomName;
            await _db.SaveChangesAsync();
            return Json(true);
        }

        // Read-only admins (GuildReadOnlyAdmin) can view MyFarms directly, so the search box that just
        // routes to MyFarms.ViewUser must allow them too - the class policy omits that tier.
        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin,GuildReadOnlyAdmin")]
        public async Task<IActionResult> SearchID([FromQuery] string id) {
            var discordIDRegex = new Regex(@"^\d+$");

            if(discordIDRegex.IsMatch(id.Trim())) {
                var userWithDiscordId = await _db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId.ToString() == id);
                if(userWithDiscordId is not null) {
                    return RedirectToAction("ViewUser", "MyFarms", new { discordId = id });
                }
            }

            var users = await _db.DBUsers.AsQueryable().ToListAsync();

            id = id.Trim();

            if(id.All(x => x >= '0' && x <= '9')) {
                return RedirectToAction("ViewUser", "MyFarms", new { discordId = id });
            } else if(Regex.IsMatch(id.ToUpper(), "EI\\d{16}") && users.Any(u => u.EggIncAccounts is not null && u.EggIncAccounts.Any(e => e.Id == id) && u.DiscordId != default)) {
                id = id.ToUpper();
                var matchingEidUser = users.FirstOrDefault(u => u.EggIncAccounts is not null && u.EggIncAccounts.Any(e => e.Id == id) && u.DiscordId != default);
                if(matchingEidUser is null) return View(new List<DBUser>());
                return RedirectToAction("ViewUser", "MyFarms", new { discordId = matchingEidUser.DiscordId });
            }

            id = id.ToLower();
            var matchingUsers = users.Where(x =>
                (x.DiscordUsername ?? "").ToLower().Contains(id) ||
                (x.Usernames ?? "").ToLower().Split(",").Any(u => u.Contains(id))
            ).ToList();

            if(matchingUsers.Count == 1) {
                return RedirectToAction("ViewUser", "MyFarms", new { discordId = matchingUsers.First().DiscordId });
            }

            return View(matchingUsers.ToList());
        }

        public async Task<ActionResult> DuplicateChannels() {


            var coops = await _db.Coops.AsQueryable().Where(x => !x.ThreadArchived && !x.DeletedChannel).ToListAsync();

            var coopChannels = _discord.Guilds.SelectMany(x => x.TextChannels);

            var coopsWithChannels = coops.Select(c => new CoopWithChannels {
                Coop = c, MainChannel = coopChannels.FirstOrDefault(x => (x.Id == c.ThreadID && c.ThreadID != 0) || (x.Id == c.DiscordChannelId && c.DiscordChannelId != 0)),
                ExtraChannels = coopChannels.Where(x => x.Id != c.ThreadID && x.Id != c.DiscordChannelId && StripEmoji(x.Name).Equals(c.Name, StringComparison.CurrentCultureIgnoreCase)).ToList()
            }).ToList();

            return View(coopsWithChannels.Where(x => x.ExtraChannels.Any()).ToList());
        }


        public async Task<ActionResult> Deleteduplicate([FromQuery] ulong id) {
            var channel = (ITextChannel)(_discord.Guilds.SelectMany(x => x.TextChannels.Where(x => x.Id == id)).First());
            await channel.DeleteAsync();
            return RedirectToAction("DuplicateChannels");
        }
        public async Task<ActionResult> DeleteAllDuplicates() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guildId);


            var coops = await _db.Coops.AsQueryable().Where(x => !x.ThreadArchived && !x.DeletedChannel).ToListAsync();

            var coopChannels = _discord.Guilds.Where(x => x.Id == guildId || dbguild.OverflowServers.Any(y => y == x.Id)).SelectMany(x => x.TextChannels);

            var coopsWithChannels = coops.Select(c => new CoopWithChannels {
                Coop = c, MainChannel = coopChannels.FirstOrDefault(x => (x.Id == c.ThreadID && c.ThreadID != 0) || (x.Id == c.DiscordChannelId && c.DiscordChannelId != 0)),
                ExtraChannels = coopChannels.Where(x => x.Id != c.ThreadID && x.Id != c.DiscordChannelId && StripEmoji(x.Name).Equals(c.Name, StringComparison.CurrentCultureIgnoreCase)).ToList()
            }).ToList();

            foreach(var channel in coopsWithChannels.Where(x => x.ExtraChannels.Any()).SelectMany(x => x.ExtraChannels)) {
                await ((ITextChannel)channel).DeleteAsync();
            }
            return RedirectToAction("DuplicateChannels");
        }


        public async Task<ActionResult> AutomatedTasks() {
            return View(await _db.AutomationLogs.Where(x => x.StartTime > DateTimeOffset.UtcNow.AddDays(-5)).OrderBy(x => x.StartTime).ToListAsync());
        }

        public class BackupWithUser {
            public DBUser User { get; set; }
            public CustomBackup Backup { get; set; }
        }

        private static string StripEmoji(string text) {
            return Regex.Replace(text, @"\p{Cs}", "");
        }

        public class CoopWithChannels {
            public Coop Coop { get; set; }
            public SocketTextChannel MainChannel { get; set; }
            public List<SocketTextChannel> ExtraChannels { get; set; }
        }

        public async Task<ActionResult> Sping2025() {
            var links = @"https://discord.com/channels/656455567858073601/761200705553432576/1192221621432352790
https://discord.com/channels/656455567858073601/1112138552013246464/1117896233147703296
https://discord.com/channels/656455567858073601/875545068348518520/1331892145816207411
https://discord.com/channels/656455567858073601/909764978901417984/909847284555079712
https://discord.com/channels/656455567858073601/807727607701438474/807729354661822484
https://discord.com/channels/656455567858073601/757878278219366431/1357126003788484649
https://discord.com/channels/656455567858073601/656455568353132546/1093966418166431804
https://discord.com/channels/656455567858073601/746509501271769210/1357732975076311111
https://discord.com/channels/656455567858073601/656455568353132546/1356601272484106320
https://discord.com/channels/656455567858073601/793657823379980318/1343599425653575711
https://discord.com/channels/656455567858073601/656455568353132546/692117278031872010
https://discord.com/channels/656455567858073601/656455568353132546/1351229314208632973
https://discord.com/channels/656455567858073601/757878278219366431/1351233781960544267
https://discord.com/channels/656455567858073601/656455568353132546/916915773572796446
https://discord.com/channels/656455567858073601/796127648899530762/1350647452667088907
https://discord.com/channels/656455567858073601/1009563842084343889/1343394249785217184
https://discord.com/channels/656455567858073601/656455568353132546/1356484053129367632
https://discord.com/channels/656455567858073601/656455568353132546/1336067384607375381
https://discord.com/channels/656455567858073601/1094454621105307718/1292837729352159232
https://discord.com/channels/656455567858073601/656455568353132546/1329735036324286484
https://discord.com/channels/656455567858073601/656455568353132546/1358254011345670205
https://discord.com/channels/656455567858073601/746509501271769210/1359506544810524773
https://discord.com/channels/656455567858073601/656455568353132546/1359696423674843228
https://discord.com/channels/656455567858073601/798985476006084628/1359701994767646730
https://discord.com/channels/656455567858073601/656455568353132546/1355557827913191446
https://discord.com/channels/656455567858073601/714900970890330132/1356308160956071996
https://discord.com/channels/656455567858073601/1062265666817753128/1193576276699643984
https://discord.com/channels/656455567858073601/792940901777014784/1224028706100740258
https://discord.com/channels/656455567858073601/796127648899530762/826991249114005584
https://discord.com/channels/656455567858073601/997065059870183454/1350910172972847189";

            var easterCacheKey = $"Sping2025";
            Dictionary<HalloweenUser, int> eggsFound;
            var dbusers = new List<DBUser>();
            if(!_cache.TryGetValue(easterCacheKey, out eggsFound)) {


                var regex = new Regex(@"(\d+)/(\d+)/(\d+)");
                var matches = regex.Matches(links);
                eggsFound = [];
                foreach(var match in matches.Cast<Match>()) {
                    var guild = await _discord.Rest.GetGuildAsync(ulong.Parse(match.Groups[1].Value));
                    var channel = await guild.GetTextChannelAsync(ulong.Parse(match.Groups[2].Value));
                    var message = await channel.GetMessageAsync(ulong.Parse(match.Groups[3].Value));
                    var reactions = message.Reactions;
                    var userReactions = await message.GetReactionUsersAsync(reactions.First(x => x.Key.Name.Contains("Easter_Egg")).Key, 9999).FlattenAsync();
                    var users = new List<IUser>();
                    foreach(var user in userReactions) {
                        if(user.Username.StartsWith("WAG", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var existingUser = eggsFound.Any(x => x.Key.User.Id == user.Id);
                        if(existingUser) {
                            eggsFound[eggsFound.First(x => x.Key.User.Id == user.Id).Key]++;
                        } else {
                            var guildUser = await guild.GetUserAsync(user.Id);
                            var dbuser = dbusers.FirstOrDefault(x => x.DiscordId == user.Id);
                            if(dbuser is null) {
                                dbuser = await _db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
                                if(dbuser is not null)
                                    dbusers.Add(dbuser);
                            }
                            if(dbuser is null || guildUser is null) continue;

                            var needsProPermit = dbuser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 0);
                            eggsFound.Add(new HalloweenUser { User = guildUser, NeedsProPermit = needsProPermit }, 1);
                        }
                        users.Add(user);
                    }
                    if(!users.Any(x => x.Username.StartsWith("WAG", StringComparison.OrdinalIgnoreCase))) {
                        Console.WriteLine(match.Value);
                    }
                }

                _cache.Set(easterCacheKey, eggsFound, TimeSpan.FromMinutes(10));
            }
            return View("HalloweenHunt", eggsFound);
        }
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
            Dictionary<HalloweenUser, int> eggsFound;
            if(!_cache.TryGetValue(easterCacheKey, out eggsFound)) {


                var regex = new Regex(@"(\d+)/(\d+)/(\d+)");
                var matches = regex.Matches(links);
                eggsFound = [];
                foreach(var match in matches.Cast<Match>()) {
                    var guild = await _discord.Rest.GetGuildAsync(ulong.Parse(match.Groups[1].Value));
                    var channel = await guild.GetTextChannelAsync(ulong.Parse(match.Groups[2].Value));
                    var message = await channel.GetMessageAsync(ulong.Parse(match.Groups[3].Value));
                    var reactions = message.Reactions;
                    var userReactions = await message.GetReactionUsersAsync(reactions.First(x => x.Key.Name.Contains("Hallowegg")).Key, 9999).FlattenAsync();
                    foreach(var user in userReactions) {
                        if(user.Username == "melina8irbie")
                            continue;
                        var existingUser = eggsFound.Any(x => x.Key.User.Id == user.Id);
                        if(existingUser) {
                            eggsFound[eggsFound.First(x => x.Key.User.Id == user.Id).Key]++;
                        } else {
                            var guildUser = await guild.GetUserAsync(user.Id);
                            var dbuser = await _db.DBUsers.FirstAsync(x => x.DiscordId == user.Id);

                            var needsProPermit = dbuser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 0);
                            eggsFound.Add(new HalloweenUser { User = guildUser, NeedsProPermit = needsProPermit }, 1);
                        }
                    }
                }

                _cache.Set(easterCacheKey, eggsFound, TimeSpan.FromMinutes(10));
            }
            return View(eggsFound);
        }

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
            Dictionary<EasterUser, int> eggsFound;
            if(!_cache.TryGetValue(easterCacheKey, out eggsFound)) {


                var regex = new Regex(@"(\d+)/(\d+)/(\d+)");
                var matches = regex.Matches(links);
                eggsFound = [];
                foreach(var match in matches.Cast<Match>()) {
                    var guild = await _discord.Rest.GetGuildAsync(ulong.Parse(match.Groups[1].Value));
                    var channel = await guild.GetTextChannelAsync(ulong.Parse(match.Groups[2].Value));
                    var message = await channel.GetMessageAsync(ulong.Parse(match.Groups[3].Value));
                    var reactions = message.Reactions;
                    var userReactions = await message.GetReactionUsersAsync(reactions.First(x => x.Key.Name.Contains("EASTER")).Key, 9999).FlattenAsync();
                    foreach(var user in userReactions) {
                        if(user.Username == "TreeGoat")
                            continue;
                        var existingUser = eggsFound.Any(x => x.Key.User.Id == user.Id);
                        if(existingUser) {
                            eggsFound[eggsFound.First(x => x.Key.User.Id == user.Id).Key]++;
                        } else {
                            var guildUser = await guild.GetUserAsync(user.Id);
                            var dbuser = await _db.DBUsers.FirstAsync(x => x.DiscordId == user.Id);

                            var needsProPermit = dbuser.EggIncAccounts.Any(x => x.Backup.PermitLevel == 0);
                            eggsFound.Add(new EasterUser { User = guildUser, NeedsProPermit = needsProPermit }, 1);
                        }
                    }
                }

                _cache.Set(easterCacheKey, eggsFound, TimeSpan.FromMinutes(10));
            }
            return View(eggsFound);
        }

        public async Task<IActionResult> ConfigureServer(ulong? id) {
            id ??= GetGuildID();
            var dbGuild = await _db.Guilds.FirstAsync(x => x.Id == id);
            return View(dbGuild);
        }

        public async Task<IActionResult> SaveChannelDetails(ulong id, [FromForm] string json) {
            if(!VerifyId(id)) {
                return NotFound();
            }
            if(string.IsNullOrEmpty(json)) {
                return BadRequest();
            }
            var model = JsonConvert.DeserializeObject<SaveChannelDetailsObject>(json);
            var dbGuild = await _db.Guilds.FirstAsync(x => x.Id == id);
            var invalidateApodGuildCache = (
                (dbGuild.ChannelDetails.FirstOrDefault(d => d.ChannelType == GuildChannelType.NasaApod)?.Id ?? ulong.MinValue)
                != (model.ChannelDetails.FirstOrDefault(d => d.ChannelType == GuildChannelType.NasaApod)?.Id ?? ulong.MinValue)
            );
            dbGuild.CoopSettings = model.CoopSettingsOverrides;
            dbGuild.ChannelDetails = model.ChannelDetails;
            dbGuild.CoopCategories = model.CoopCategories;
            dbGuild.FinishedCategories = model.FinishedCategories;
            dbGuild.DisableBG = model.DisableBG;
            dbGuild.GroupRoles = model.GroupRoles;
            dbGuild.AllowGuilds = model.AllowGuilds;
            dbGuild.PublicScoreGrid = model.PublicScoreGrid;
            dbGuild.RemoveFindCoopSpot = model.RemoveFindCoopSpot;
            dbGuild.CoopNamePrefix = string.IsNullOrWhiteSpace(model.CoopNamePrefix) ? null : model.CoopNamePrefix;
            dbGuild.AddOutsideCoops = model.AddOutsideCoops;
            dbGuild.MinimumRunningScore = model.MinimumRunningScore;
            Console.WriteLine("Setting FAQTopicsEnabled to " + model.FAQTopicsEnabled);
            Console.WriteLine("Setting FAQTopicCooldownMinutes to " + model.FAQTopicCooldownMinutes);
            dbGuild.FAQTopicsEnabled = model.FAQTopicsEnabled;
            dbGuild.FAQTopicCooldownMinutes = model.FAQTopicCooldownMinutes;
            if(invalidateApodGuildCache) {
                var guildNasaKey = _db.InvalidateGuildNASACache(dbGuild);
                await _publishEndpoint.Publish(new ExpireCacheMessage(guildNasaKey));
            }
            await _db.SaveChangesAsync();

            return Ok();
        }
        public async Task<IActionResult> SaveRolesToSync(ulong id, [FromForm] string rolestosync) {
            if(!VerifyId(id)) {
                return NotFound();
            }
            var dbGuild = await _db.Guilds.FirstAsync(x => x.Id == id);
            dbGuild.RolesToSync = rolestosync;
            await _db.SaveChangesAsync();

            return Ok();
        }

        public ulong GetGuildID() {
            return ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
        }

        public bool VerifyId(ulong guildid) {
            if(User.IsInRole("Admin"))
                return true;
            return ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value) == guildid;
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

        public class SaveChannelDetailsObject {
            public List<ServerCoopSetting> CoopSettingsOverrides { get; set; }
            public List<ChannelDetail> ChannelDetails { get; set; }
            public string CoopCategories { get; set; }
            public string FinishedCategories { get; set; }
            public bool DisableBG { get; set; }
            public string GroupRoles { get; set; }
            public bool AllowGuilds { get; set; }
            public bool PublicScoreGrid { get; set; }
            public string CoopNamePrefix { get; set; }
            public bool RemoveFindCoopSpot { get; set; }
            public bool AddOutsideCoops { get; set; }
            public bool FAQTopicsEnabled { get; set; }
            public int FAQTopicCooldownMinutes { get; set; }
            public float MinimumRunningScore { get; set; }
        }

        public class EasterUser {
            public RestGuildUser User { get; set; }
            public bool NeedsProPermit { get; set; }
        }

        public class HalloweenUser {
            public RestGuildUser User { get; set; }
            public bool NeedsProPermit { get; set; }
        }

        public async Task<IActionResult> StandardPermit() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var users = await _db.DBUsers.Where(x => x.GuildId == guildId && !x.TempDisabled).ToListAsync();
            users = users.Where(x => x.EggIncAccounts.Any(y => y.Backup.PermitLevel == 0)).ToList();

            var userids = users.Select(x => x.Id).ToArray();
            ViewBag.Demerits = await _db.Demerit.Where(x => userids.Contains(x.UserId)).ToListAsync();
            ViewBag.Merits = await _db.Merit.Where(x => userids.Contains(x.UserId)).ToListAsync();
            ViewBag.Xrefs = await _db.UserCoopXrefs.Where(x => x.Score.HasValue && userids.Contains(x.UserId)).ToListAsync();

            return View(users);
        }

        public async Task<IActionResult> InactivePlayers() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            // The view only reads Id, DiscordId, TempDisabled, Notes and account ids, so project
            // those columns instead of every user's full row (ship-DM / coop-setting / backup blobs).
            var users = (await _db.DBUsers
                .Select(u => new { u.Id, u.DiscordId, u.TempDisabled, u.Notes, u._eggIncIds, u._contractRegistrationByte })
                .ToListAsync())
                .Select(u => {
                    var row = DBUser.FromAccountColumns(u._eggIncIds, u._contractRegistrationByte);
                    row.Id = u.Id;
                    row.DiscordId = u.DiscordId;
                    row.TempDisabled = u.TempDisabled;
                    row.Notes = u.Notes;
                    return row;
                })
                .ToList();
            var guild = _discord.Guilds.First(x => x.Id == guildId);
            await guild.DownloadUsersAsync();
            // Latest xref per user. Was raw SQL with unquoted PascalCase identifiers, which
            // Postgres folds to lowercase (42P01 "usercoopxrefs does not exist"). Materialize
            // once then group client-side; the view only needs the most recent xref per user.
            var xrefs = (await _db.UserCoopXrefs.AsQueryable().ToListAsync())
                .GroupBy(x => x.UserId)
                .Select(g => g.OrderByDescending(x => x.CreatedOn).First())
                .ToList();

            return View((users, guild.Users, xrefs));
        }

        public async Task<IActionResult> NonServerUsers() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = _discord.Guilds.First(x => x.Id == guildId);
            await guild.DownloadUsersAsync();

            // A partial roster cannot tell "left" from "not yet downloaded"; refuse to list
            // so staff never unassign a real member during an incomplete cache.
            if(!guild.HasAllMembers) {
                return View((new List<DBUser>(), true));
            }

            var memberIds = guild.Users.Select(u => u.Id).ToHashSet();

            var rows = (await _db.DBUsers
                .Where(u => u.GuildId == guildId)
                .Select(u => new { u.Id, u.DiscordId, u.DiscordUsername, u._eggIncIds, u._contractRegistrationByte })
                .ToListAsync())
                .Where(u => !memberIds.Contains(u.DiscordId))
                .Select(u => {
                    var row = DBUser.FromAccountColumns(u._eggIncIds, u._contractRegistrationByte);
                    row.Id = u.Id;
                    row.DiscordId = u.DiscordId;
                    row.DiscordUsername = u.DiscordUsername;
                    return row;
                })
                .ToList();

            return View((rows, false));
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> RemoveServer([FromQuery] Guid UserId) {
            var user = await _db.DBUsers.FirstAsync(x => x.Id == UserId);
            if(!VerifyId(user.GuildId)) {
                return NotFound();
            }
            user.LastGuild = user.GuildId;
            user.GuildId = 0;
            await _db.SaveChangesAsync();
            return RedirectToAction("NonServerUsers");
        }

        public async Task<IActionResult> SaveNotes([FromQuery] Guid UserId, [FromQuery] string Notes) {
            var user = await _db.DBUsers.FirstAsync(x => x.Id == UserId);
            if(!VerifyId(user.GuildId)) {
                return NotFound();
            }
            user.Notes = Notes;
            await _db.SaveChangesAsync();
            return Content("Success");
        }

        public IActionResult Sync() {
            var url = Url.ActionLink("DiscordReturn");
            return Redirect($"https://discordapp.com/api/oauth2/authorize?response_type=code&client_id={_configuration.GetConnectionString("ClientId")}&scope=identify%20guilds.join%20applications.commands.permissions.update&state=15773059ghq9183habn&redirect_uri={url}");
        }

        public async Task<IActionResult> DiscordReturn() {
            string code = Request.Query["code"];

            // Get Access Token from authorization code by making an HTTP POST request
            var url = "https://discordapp.com/api/oauth2/token";
            var parameters = $"client_id={_configuration.GetConnectionString("ClientId")}&client_secret={_configuration.GetConnectionString("ClientSecret")}&grant_type=authorization_code&code={code}&redirect_uri={Url.ActionLink("DiscordReturn")}";

            using var httpClient = new HttpClient();
            var content = new StringContent(parameters, Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await httpClient.PostAsync(url, content);

            if(response.IsSuccessStatusCode) {
                var responseContent = await response.Content.ReadAsStringAsync();
                dynamic jsonObject = JsonConvert.DeserializeObject(responseContent);
                string access_token = jsonObject.access_token;

                return Redirect($"/admin/SyncCommandPermissions?access_token={access_token}");
            } else {
                return BadRequest("Failed to retrieve access token.");
            }
        }

        public async Task<IActionResult> SyncCommandPermissions(string access_token) {
            var guild = await _db.Guilds.FirstAsync(x => x.Id == GetGuildID());
            if(guild.RolesToSync is null)
                return Content("No roles found to sync");
            var roleids = guild.RolesToSync.Split(",");
            var mainServer = _discord.Guilds.First(x => x.Id == guild.Id);
            var overflowServers = _discord.Guilds.Where(x => guild.OverflowServers.Contains(x.Id));
            var rolesToSync = mainServer.Roles.Where(x => roleids.Any(y => y == x.Id.ToString()));

            var roleMaps = OverflowSyncing.GetRoleMaps(rolesToSync.ToList(), overflowServers);
            var output = await OverflowSyncing.HandleCommandPermissionSyncsAsync(guild, mainServer, overflowServers, roleMaps, access_token, _configuration.GetConnectionString("Token"));

            return Content(output);
        }

        public async Task<IActionResult> Guilds() {
            var users = await _db.DBUsers.Where(x => x.GuildId == GetGuildID()).ToListAsync();
            return View(users);
        }

        public async Task<IActionResult> CheckCoopCreators() {
            var creators = new List<(string EggIncId, PlayerGrade Grade, string Name, ContractPlayerInfo Info)>();
            foreach(var a in EggIncApi.CoopCreatorIds) {
                var r = await EggIncApi.Post<ContractPlayerInfo, BasicRequestInfo>(new BasicRequestInfo(), a.EggIncId);
                creators.Add((a.EggIncId, a.Grade, a.Name, r));
            }

            return View(creators);
        }
    }
}
