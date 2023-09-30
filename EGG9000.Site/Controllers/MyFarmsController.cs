using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Services;


using MessagePack;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EGG9000.Common.Helpers;
using Discord.WebSocket;
using Newtonsoft.Json;
using Ei;
using Stripe;
using System.Security.Principal;
using Event = EGG9000.Common.Database.Entities.Event;
using System.Diagnostics.Contracts;
using static EGG9000.Site.Controllers.HomeController;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace EGG9000.Site.Controllers {
    [Authorize]
    public class MyFarmsController : Controller {
        private readonly ILogger<MyFarmsController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly APILink _apiLink;
        private readonly DiscordSocketClient _discord;

        public MyFarmsController(
            ILogger<MyFarmsController> logger,
            UserManager<IdentityUser> userManager,
            DiscordSocketClient discord,
            RoleManager<IdentityRole> roleManager,
            APILink apiLink,
            ApplicationDbContext db) {
            _roleManager = roleManager;
            _userManager = userManager;
            _logger = logger;
            _apiLink = apiLink;
            _db = db;
            _discord = discord;
        }

        public async Task<IActionResult> Index() {
            var loginuser = (await _userManager.GetUserAsync(User));

            var logins = await _userManager.GetLoginsAsync(loginuser);
            return await ViewUser(ulong.Parse(logins.First().ProviderKey));
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> ViewUser(ulong discordId) {
            var user = await _db.DBUsers.Include(x => x.UserCoopXrefs).ThenInclude(x => x.Coop).FirstOrDefaultAsync(x => x.DiscordId == discordId);
            bool update = false;
            var rawBackups = new List<Ei.Backup>();
            var scoring = new List<(string EggIncId, MyContracts MyContracts)>();
            foreach(var account in user.EggIncAccounts) {
                var rawBackup = await ContractsAPI.FirstContact(account.Id);
                rawBackups.Add(rawBackup.Backup);
                var customBackup = new CustomBackup(rawBackup.Backup);
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

                var scores = await ContractsAPI.Post<MyContracts, BasicRequestInfo>(new BasicRequestInfo(), account.Id);

                scoring.Add((account.Id, scores));
            }
            if(update) {
                user.UpdateAccounts();
                await _db.SaveChangesAsync();
            }
            var contractIDs = user.EggIncAccounts.Where(x => x.Backup is not null).SelectMany(b => b.Backup.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId)).ToList();
            var Contracts = await _db.Contracts.AsQueryable().ToListAsync();
            var Demerits = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            var Merits = await _db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            var RawBackups = rawBackups;
            var Snapshots = await _db.UserSnapShots.AsQueryable().Where(x => x.UserId == user.Id).ToListAsync();
            var xrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => x.UserId == user.Id && !x.Coop.DeletedChannel && !x.JoinedCoop).Include(x => x.Coop).ThenInclude(x => x.Contract).ToListAsync();
            var coops = await _db.Coops.Where(x => x.UserCoopsXrefs.Any(y => y.UserId == user.Id && y.JoinedCoop) && !x.DeletedChannel).Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).ToListAsync();
            var EpicResearchConfig = EpicResearchCalc.GetEpicResearchConfig();
            var Scoring = scoring;
            var DbGuild = await _db.Guilds.FirstOrDefaultAsync(x => x.Id == user.GuildId);
            var uncompletedPes = GetUncompletedPEContracts(user, Contracts);

            return View("Index", new MyFarmsModel(user, Contracts, Demerits, Merits, RawBackups, Snapshots, xrefs, coops, EpicResearchConfig, scoring, DbGuild, uncompletedPes));
        }

        [AllowAnonymous]
        public async Task<IActionResult> ViewUserId(string eggIncId) {
            if(!eggIncId.StartsWith("EI")) {
                return Content("EggIncID doesn't start with EI");
            }
            //var user = await _db.DBUsers.Include(x => x.UserCoopXrefs).ThenInclude(x => x.Coop).FirstOrDefaultAsync(x => x.DiscordId == discordId);
            var rawBackup = await ContractsAPI.FirstContact(eggIncId);
            var backup = new CustomBackup(rawBackup.Backup);
            var RawBackups = new List<Ei.Backup>() { rawBackup.Backup };
            var contractIDs = backup.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId).ToList();
            var Contracts = await _db.Contracts.AsQueryable().ToListAsync();
            var serialized = MessagePackSerializer.Serialize(backup, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
            backup = MessagePackSerializer.Deserialize<CustomBackup>(serialized, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
            var newDbUser = new DBUser {
                EggIncAccounts = new List<EggIncAccount> { new EggIncAccount { Backup = backup } },
                UserCoopXrefs = new List<UserCoopXref>()
            };
            var uncompletedPes = GetUncompletedPEContracts(newDbUser, Contracts);
            return View("Index", new MyFarmsModel(
                newDbUser, Contracts, new List<Demerit>(), new List<Merit>(), RawBackups, new List<UserSnapShot>(), new List<UserCoopXref>(), new List<Coop>(), new List<EpicResearchCalc.EpicResearchDetail>(), new List<(string EggIncId, MyContracts MyContracts)>(), null,
                uncompletedPes
            ));
        }

        public record MyFarmsModel(
            DBUser User,
            List<Common.Database.Entities.Contract> Contracts,
            List<Demerit> Demerits,
            List<Merit> Merits,
            List<Backup> RawBackups,
            List<UserSnapShot> SnapShots,
            List<UserCoopXref> UnjoinedCoops,
            List<Coop> JoinedCoops,
            List<EpicResearchCalc.EpicResearchDetail> EpicResearchConfig,
            List<(string EggIncId, MyContracts MyContracts)> Scoring,
            Guild DBGuild,
            Dictionary<string, List<Common.Database.Entities.Contract>> UncompletedPEContracts
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
                Event = boostEvent
            });
        }

        public async Task<IActionResult> GetCustomBackup([FromQuery] string id) {
            //EI5862923193024512
            var rawBackup = await ContractsAPI.FirstContact(id);
            var customBackup = new CustomBackup(rawBackup.Backup);
            return Json(customBackup);
        }

        public class EarningsBoostCalculatorModel {
            public CustomBackup Backup { get; set; }
            public Event Event { get; set; }
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

        public Dictionary<string, List<Common.Database.Entities.Contract>> GetUncompletedPEContracts(DBUser user,  List<Common.Database.Entities.Contract> contracts) {
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
                    var dmChannel = await discordUser.CreateDMChannelAsync();
                    try {
                        await dmChannel.SendMessageAsync("Testing DM Ping");
                    } catch(Exception ex) {
                        var dbUser = _db.DBUsers.FirstOrDefault(u => u.DiscordId == discordUser.Id);
                        if(dbUser is not null) {
                            dbUser.DMSBlocked = true;
                            await _db.SaveChangesAsync();
                        }
                    }
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
            return View(coop);
        }
    }
}