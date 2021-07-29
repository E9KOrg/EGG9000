using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Discord.WebSocket;

using DiscordCoopCodes;
using DiscordCoopCodes.Database;
using DiscordCoopCodes.Database.Entities;

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
            var contracts = await _db.GuildContracts.Include(x => x.Contract).Where(x => x.GuildID == guildId && x.Contract.Created > DateTimeOffset.Now.AddDays(-31)).OrderByDescending(x => x.Contract.Created).ToListAsync();
            return View(contracts);
        }

        public async Task<IActionResult> Coop([FromQuery]ulong GuildId, [FromQuery]String ContractID, [FromQuery]bool Elite) {
            if(User.IsInRole("Admin") || User.IsInRole("GuildAdmin") || true) {
                var stopwatch = Stopwatch.StartNew();
                var guildContract = await _db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == ContractID && x.GuildID == GuildId && x.Elite == Elite);
                Console.WriteLine($"GuildContract: {stopwatch.ElapsedMilliseconds}ms"); stopwatch.Restart();
                
                var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6) && x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
                Console.WriteLine($"Coops: {stopwatch.ElapsedMilliseconds}ms");
                stopwatch.Restart();
                //var users = await _db.Users.Where(x => x.GuildId == GuildId).ToListAsync();
                var rawusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == GuildId).Select(x => new {
                    x.DiscordId, x.DiscordUsername, x.GuildId, x.Id, x._CustomBackups, x._eggIncIds, x.TempDisabled
                }).ToListAsync();
                var dbusers = rawusers.Select(x => new DBUser { TempDisabled = x.TempDisabled, DiscordId = x.DiscordId, DiscordUsername = x.DiscordUsername, GuildId = x.GuildId, Id = x.Id, _CustomBackups = x._CustomBackups, _eggIncIds = x._eggIncIds });
                var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser {
                    User = y,
                    Backup = x
                })).ToList();
                Console.WriteLine($"Backups: {stopwatch.ElapsedMilliseconds}ms");
                stopwatch.Restart();
                var prefarms = await GetPrefarmers(backups, guildContract.Contract);
                Console.WriteLine($"Prefarmers: {stopwatch.ElapsedMilliseconds}ms");
                stopwatch.Restart();
                ViewBag.Discord = _discord;

                return View(new CoopsViewModel {
                    Coops = coops,
                    GuildContract = guildContract,
                    PreFarms = prefarms
                });
            } else {
                return View("TempDisabled");
            }

        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> StartCoop([FromBody]List<UserPreFarm> Users, [FromQuery] ulong GuildId, [FromQuery] String ContractID, [FromQuery] bool Elite) {
            var guildContract = await _db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == ContractID && x.GuildID == GuildId && x.Elite == Elite);
            var guild = _discord.GetGuild(guildContract.GuildID);

            var coop = await CreateCoops.Start(Users, guildContract, guild, new Words(), _db);
            guildContract.NumberOfCoops--;
            await _db.SaveChangesAsync();

            if(guildContract != null) {
                var guildContractChannel = guild.TextChannels.FirstOrDefault(x => x.Id == guildContract.DiscordChannelId);
                guildContractChannel?.SendMessageAsync($"Created co-op for {string.Join(", ", Users.Select(x => x.Name.Trim()))} via website");
            }


            return Json(coop.Name);
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> MoveToCoop([FromQuery]Guid CoopId, [FromQuery] Guid UserId, [FromQuery] String EggIncId) {
            var targetCoop = await _db.Coops.Include(x => x.Contract).AsQueryable().FirstAsync(x => x.Id == CoopId);
            var dbuser = await _db.DBUsers.AsQueryable().FirstAsync(x => x.Id == UserId);
            var guild = _discord.GetGuild(targetCoop.GuildId);
            var discordUser = guild.Users.First(x => x.Id == dbuser.DiscordId);
            var guildId = targetCoop.OverflowGuildId > 0 ? targetCoop.OverflowGuildId : targetCoop.GuildId;
            var channel = _discord.GetGuild(guildId).TextChannels.First(x => x.Id == targetCoop.DiscordChannelId);
            var eggIncName = dbuser.EggIncIds.First(x => x.Id == EggIncId).Name;
            var xref = await CreateCoops.MoveUser(targetCoop, UserId, EggIncId, eggIncName, discordUser, dbuser, channel, null);

            if(xref == null) {
                return Json(new { Error = $"Unable to add permissions for {dbuser.DiscordUsername}, likely not in overflow server"});
            }

            _db.Add(xref);
            await _db.SaveChangesAsync();

            var guildContract = await _db.GuildContracts.AsQueryable().FirstOrDefaultAsync(x => x.ContractID == targetCoop.ContractID && x.GuildID == guild.Id && x.Elite == (targetCoop.League == 0));

            if(guildContract != null) {
                var guildContractChannel = _discord.GetGuild(guildId).TextChannels.FirstOrDefault(x => x.Id == guildContract.DiscordChannelId);
                guildContractChannel?.SendMessageAsync($"Moved {dbuser.DiscordUsername} via website");
            }

            return Json(new {
                UserName = dbuser.DiscordUsername, CoopName = targetCoop.Name
            });
        }

        public class CoopsViewModel {
            public List<Coop> Coops { get; set; }
            public GuildContract GuildContract { get; set; }
            public List<UserPreFarm> PreFarms { get; set; }
        }
    }
}
