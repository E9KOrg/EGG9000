using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Services;


using MessagePack;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EGG9000.Common.Helpers;

namespace EGG9000.Site.Controllers {
    [Authorize]
    public class MyFarmsController : Controller {
        private readonly ILogger<MyFarmsController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly APILink _apiLink;

        public MyFarmsController(
            ILogger<MyFarmsController> logger,
            UserManager<IdentityUser> userManager,
            RoleManager<IdentityRole> roleManager,
            APILink apiLink,
            ApplicationDbContext db) {
            _roleManager = roleManager;
            _userManager = userManager;
            _logger = logger;
            _apiLink = apiLink;
            _db = db;
        }

        public async Task<IActionResult> Index() {
            var loginuser = (await _userManager.GetUserAsync(User));

            var logins = await _userManager.GetLoginsAsync(loginuser);
            return await ViewUser(ulong.Parse(logins.First().ProviderKey));
        }

        [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
        public async Task<IActionResult> ViewUser(ulong discordId) {
            var user = await _db.DBUsers.Include(x => x.UserCoopXrefs).ThenInclude(x => x.Coop).FirstOrDefaultAsync(x => x.DiscordId == discordId);
            var backups = new List<CustomBackup>();
            var rawBackups = new List<Ei.Backup>();
            foreach(var accounts in user.EggIncIds) {
                var rawBackup = await ContractsAPI.FirstContact(accounts.Id);
                rawBackups.Add(rawBackup.Backup);
                var customBackup = new CustomBackup(rawBackup.Backup);
                //var json = JsonSerializer.Serialize(customBackup);
                //var json = Newtonsoft.Json.JsonConvert.SerializeObject(customBackup);
                //var customBackupAfterJson = Newtonsoft.Json.JsonConvert.DeserializeObject<CustomBackup>(json);

                //var response = await _apiLink.GetBackup(accounts.Id);
                Console.WriteLine($"Getting backups for {accounts.Name}");
                if(customBackup?.SpaceMissions != null) {
                    backups.Add(customBackup);
                }
                //Console.WriteLine(customBackup.SpaceMissions.Count);
            }
            user.Backups = backups;
            await _db.SaveChangesAsync();
            var contractIDs = user.Backups.SelectMany(b => b.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId)).ToList();
            ViewBag.Contracts = await _db.Contracts.AsQueryable().ToListAsync();
            ViewBag.Demerits = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            ViewBag.Merits = await _db.Merit.AsQueryable().Where(x => x.UserId == user.Id).OrderBy(x => x.When).ToListAsync();
            ViewBag.RawBackups = rawBackups;
            ViewBag.Snapshots = await _db.UserSnapShots.AsQueryable().Where(x => x.UserId == user.Id).ToListAsync();
            ViewBag.Coops = await _db.UserCoopXrefs.AsQueryable().Where(x => x.UserId == user.Id && !x.JoinedCoop && !x.Coop.DeletedChannel).Include(x => x.Coop).ThenInclude(x => x.Contract).ToListAsync();
            ViewBag.EpicResearchConfig = EpicResearchCalc.GetEpicResearchConfig();
            return View("Index", user);
        }

        [AllowAnonymous]
        public async Task<IActionResult> ViewUserId(string eggIncId) {
            //var user = await _db.DBUsers.Include(x => x.UserCoopXrefs).ThenInclude(x => x.Coop).FirstOrDefaultAsync(x => x.DiscordId == discordId);
            var rawBackup = await ContractsAPI.FirstContact(eggIncId);
            var backup = new CustomBackup(rawBackup.Backup);
            ViewBag.RawBackups = new List<Ei.Backup>() { rawBackup.Backup };
            var contractIDs = backup.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId).ToList();
            ViewBag.Contracts = await _db.Contracts.AsQueryable().ToListAsync();
            var serialized = MessagePackSerializer.Serialize(backup, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
            backup = MessagePackSerializer.Deserialize<CustomBackup>(serialized, MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray));
            return View("Index", new DBUser {
                Backups = new List<CustomBackup> { backup },
                UserCoopXrefs = new List<UserCoopXref>()
            });
        }

        public async Task<IActionResult> EarningsBoostCalculator() {
            var loginuser = (await _userManager.GetUserAsync(User));

            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            //Get fresh backups
            var backups = new List<CustomBackup>();
            foreach(var accounts in user.EggIncIds) {
                backups.Add((await _apiLink.GetBackup(accounts.Id)));
            }
            user.Backups = backups;

            var contractIDs = user.Backups.SelectMany(b => b.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId)).ToList();
            ViewBag.Contracts = await _db.Contracts.AsQueryable().Where(x => contractIDs.Contains(x.ID)).ToListAsync();
            //user.Backups.ForEach(b => b.Contracts.Contracts.ToList().ForEach(c => c.Contract.Name = contracts.FirstOrDefault(x => x.ID == c.Contract.Identifier)?.Name));

            var boostEvent = await _db.Events.AsQueryable().Where(x => x.Type == "earnings-boost" && !x.Ended && x.Ends > DateTimeOffset.Now).FirstOrDefaultAsync();

            return View(new EarningsBoostCalculatorModel {
                Backups = user.Backups,
                Event = boostEvent
            });
        }


        public async Task<IActionResult> Bob4() {
            var loginuser = (await _userManager.GetUserAsync(User));

            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            //Get fresh backups
            var backups = new List<CustomBackup>();
            foreach(var accounts in user.EggIncIds) {
                backups.Add((await _apiLink.GetBackup(accounts.Id)));
            }
            user.Backups = backups;

            var contractIDs = user.Backups.SelectMany(b => b.Farms.Where(f => f.FarmType == Ei.FarmType.Contract).Select(f => f.ContractId)).ToList();
            ViewBag.Contracts = await _db.Contracts.AsQueryable().Where(x => contractIDs.Contains(x.ID)).ToListAsync();
            //user.Backups.ForEach(b => b.Contracts.Contracts.ToList().ForEach(c => c.Contract.Name = contracts.FirstOrDefault(x => x.ID == c.Contract.Identifier)?.Name));

            return View(new EarningsBoostCalculatorModel {
                Backups = user.Backups,
            });
        }

        public class EarningsBoostCalculatorModel {
            public List<CustomBackup> Backups { get; set; }
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

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> RemoveMerit([FromQuery] Guid id) {
            var merit = _db.Merit.FirstOrDefault(x => x.Id == id);
            _db.Remove(merit);
            await _db.SaveChangesAsync();
            return Redirect(Request.Headers["Referer"].ToString());
        }
    }
}