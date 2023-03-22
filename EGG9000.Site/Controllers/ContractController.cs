using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Discord.WebSocket;

using EGG9000.Bot;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using EGG9000.Common.Helpers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Controllers {
    [Authorize]
    public class ContractController : Controller {
        private readonly ApplicationDbContext _db;
        private readonly DiscordSocketClient _discord;

        public ContractController(
            ApplicationDbContext db,
            DiscordSocketClient discord
            ) {
            _db = db;
            _discord = discord;
        }

        public async Task<IActionResult> Index() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var contracts = await _db.GuildContracts.Include(x => x.Contract).Where(x => x.GuildID == guildId && x.Contract.Created > DateTimeOffset.Now.AddMonths(-2)).OrderByDescending(x => x.Contract.Created).ToListAsync();
            return View(contracts);
        }

        public IActionResult Coop([FromQuery] ulong GuildId, [FromQuery] String ContractID, [FromQuery] bool Elite) {
            return RedirectPermanent($"/Contract/Details{Request.QueryString}");
        }

        [Produces("application/json")]
        public async Task<IActionResult>CoopStatusJson(string coopid, string contractid) {
            var status = await ContractsAPI.GetCoopStatus(contractid, coopid);
            return new ObjectResult(status);
        }

        public async Task<IActionResult> Details([FromQuery] ulong GuildId, [FromQuery] String ContractID, [FromQuery] bool Elite) {
            if(User.IsInRole("Admin") || User.IsInRole("GuildAdmin") || true) {
                await _discord.Guilds.First(x => x.Id == GuildId).DownloadUsersAsync();

                var guildContract = await _db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == ContractID && x.GuildID == GuildId && x.Elite == Elite);


                var coopsBreakdown = await Prefarm.GetBreakdown(_db, guildContract, _discord);

                ViewBag.Discord = _discord;



                return View(new CoopsViewModel {
                    GuildContract = guildContract,
                    CoopsBreakdown = coopsBreakdown
                });
            } else {
                return View("TempDisabled");
            }

        }

        public async Task<IActionResult> ScoreGraph([FromQuery] ulong GuildId, [FromQuery] String ContractID) {
            var contract = await _db.Contracts.AsQueryable().FirstOrDefaultAsync(x => x.ID == ContractID);
            ViewBag.Contract = contract;
            var claimsIdentity = (System.Security.Claims.ClaimsIdentity)User.Identity;
            var discordId = ulong.Parse(claimsIdentity.Claims.First(x => x.Type == "DiscordId").Value);

            var scores = await _db.UserCoopXrefs.Where(x => x.Coop.ContractID == ContractID && x.User.GuildId == GuildId && x.Score.HasValue).Select(x => new ScoreGraphItem {
                Score = x.Score,
                RunningScore = x.RunningScore,
                CurrentUser = x.User.DiscordId == discordId,
                SoulPower = x.SoulPower
            }).ToListAsync();

            return View(scores);
        }

        public class ScoreGraphItem {
            public bool CurrentUser { get; set; }
            public float? Score { get; set; }
            public float? RunningScore { get; set; }
            public double? SoulPower { get; set; }
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> StartCoop([FromBody] List<UserPreFarm> Users, [FromQuery] ulong GuildId, [FromQuery] String ContractID, [FromQuery] bool Elite) {
            var guildContract = await _db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == ContractID && x.GuildID == GuildId && x.Elite == Elite);
            var guild = _discord.GetGuild(guildContract.GuildID);


            var eggIncIDs = Users.Select(x => x.EggIncId);
            var existingsXrefs = await _db.UserCoopXrefs.Include(x => x.User).Include(x => x.Coop).AsQueryable().Where(x => x.Coop.Created > DateTimeOffset.Now.AddMonths(-6) && x.Coop.ContractID == ContractID && eggIncIDs.Contains(x.EggIncId) && x.Coop.Status != CoopStatusEnum.Failed).ToListAsync();
            if(existingsXrefs.Count > 0) {
                return Json(new { error = true, message = $"Un-able to create co-op, the following are already in one: {string.Join(", ", existingsXrefs.Select(x => x.User.DiscordUsername))}" });
            }

            var dbUserIds = Users.Select(x => x.DatabaseId).ToList();
            var dbusers = await _db.DBUsers.Where(x => dbUserIds.Contains(x.Id)).ToListAsync();
            var userswithbackups = dbusers.SelectMany(x => x.Backups.Select(y => new UserWithBackup { User = x, Backup = y }));
            var userdetails = Users.Select(x => new UserFarmDetails(guildContract.Contract, userswithbackups.First(y => y.Backup.EggIncId == x.EggIncId && y.User.Id == x.DatabaseId), _discord, Elite ? 0 : 1)).ToList();

            var coop = await CreateCoops.Start(userdetails, guildContract, guild, new Words(), _db);
            guildContract.NumberOfCoops--;
            await _db.SaveChangesAsync();

            if(guildContract != null) {
                var guildContractChannel = guild.TextChannels.FirstOrDefault(x => x.Id == guildContract.DiscordChannelId);
                guildContractChannel?.SendMessageAsync($"Created co-op for {string.Join(", ", Users.Select(x => x.Name.Trim()))} via website");
            }


            return Json(new { coopName = coop.Name });
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> MoveToCoop([FromQuery] Guid CoopId, [FromQuery] Guid UserId, [FromQuery] String EggIncId) {
            var targetCoop = await _db.Coops.Include(x => x.Contract).AsQueryable().FirstAsync(x => x.Id == CoopId);
            var dbuser = await _db.DBUsers.AsQueryable().FirstAsync(x => x.Id == UserId);

            var existingXref = await _db.UserCoopXrefs.AsQueryable().FirstOrDefaultAsync(x => x.Coop.Created > DateTimeOffset.Now.AddMonths(-6) && x.Coop.ContractID == targetCoop.ContractID && x.EggIncId == EggIncId && x.Coop.Status != CoopStatusEnum.Failed);
            if(existingXref != null) {
                return Json(new { error = $"{dbuser.DiscordUsername} has already been assigned a co-op." });
            }

            var guild = _discord.GetGuild(targetCoop.GuildId);
            var discordUser = guild.Users.First(x => x.Id == dbuser.DiscordId);
            var guildId = targetCoop.OverflowGuildId > 0 ? targetCoop.OverflowGuildId : targetCoop.GuildId;

            var channel = (SocketTextChannel)_discord.GetChannel(targetCoop.DiscordChannelId);
            var eggIncName = dbuser.EggIncIds.First(x => x.Id == EggIncId).Name;
            var xref = await CreateCoops.MoveUser(targetCoop, UserId, EggIncId, eggIncName, discordUser, dbuser, channel, null);

            if(xref == null) {
                return Json(new { error = $"Unable to add permissions for {dbuser.DiscordUsername}, likely not in overflow server" });
            }

            _db.Add(xref);
            await _db.SaveChangesAsync();

            var guildContract = await _db.GuildContracts.AsQueryable().FirstOrDefaultAsync(x => x.ContractID == targetCoop.ContractID && x.GuildID == guild.Id && x.Elite == (targetCoop.League == 0));

            if(guildContract != null) {
                var guildContractChannel = (SocketTextChannel)_discord.GetChannel(guildContract.DiscordChannelId);
                guildContractChannel?.SendMessageAsync($"Moved {dbuser.DiscordUsername} via website");
            }

            return Json(new {
                UserName = dbuser.DiscordUsername,
                CoopName = targetCoop.Name
            });
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> RemoveXref([FromBody] RemoveXrefModel model) {
            var xref = await _db.UserCoopXrefs.AsQueryable().FirstOrDefaultAsync(x => x.UserId == model.UserId && x.CoopId == model.CoopId && x.EggIncId == model.EggIncId);
            if(xref == null) {
                return Json(new { error = $"Unable to find xref." });
            }
            _db.Remove(xref);
            await _db.SaveChangesAsync();
            var xref2 = await _db.UserCoopXrefs.AsQueryable().FirstOrDefaultAsync(x => x.UserId == model.UserId && x.CoopId == model.CoopId && x.EggIncId == model.EggIncId);
            Console.WriteLine($"xref2 {xref2}");
            return Json(new { Success = true });
        }

        public class RemoveXrefModel {
            public Guid UserId { get; set; }
            public Guid CoopId { get; set; }
            public string EggIncId { get; set; }
        }

        public class CoopsViewModel {
            public List<Coop> Coops { get; set; }
            public GuildContract GuildContract { get; set; }
            public CoopsBreakdown CoopsBreakdown { get; set; }
            public List<UserPreFarm> UserPreFarms { get; set; }
        }
    }
}
