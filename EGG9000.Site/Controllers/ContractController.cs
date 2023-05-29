using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper.Internal;

using Discord;
using Discord.WebSocket;

using EGG9000.Bot;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using EGG9000.Common.Helpers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using EGG9000.Bot.Helpers;
using static EGG9000.Common.Helpers.Prefarm;
using Newtonsoft.Json;
using EGG9000.Common.Migrations;

namespace EGG9000.Site.Controllers {
    [Authorize]
    public class ContractController : Controller {
        private readonly ApplicationDbContext _db;
        private readonly DiscordSocketClient _discord;
        private readonly Bugsnag.IClient _bugsnag;
        private readonly IServiceProvider _provider;
        public ContractController(
            ApplicationDbContext db,
            DiscordSocketClient discord, Bugsnag.IClient bugsnag, IServiceProvider provider
            ) {
            _db = db;
            _discord = discord;
            _bugsnag = bugsnag;
            _provider = provider;
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
        public async Task<IActionResult> CoopStatusJson(string coopid, string contractid) {
            var status = await ContractsAPI.GetCoopStatus(contractid, coopid);
            return new ObjectResult(status);
        }


        //public async Task<IActionResult> Day1Coops([FromQuery] ulong GuildId, [FromQuery] uint size, [FromQuery] string contractid) {

        //    Ei.Contract contract;

        //    if(string.IsNullOrWhiteSpace(contractid)) {
        //        contract = new Ei.Contract();
        //        foreach(Ei.Contract.Types.PlayerGrade grade in Enum.GetValues(typeof(Ei.Contract.Types.PlayerGrade))) {
        //            var gradeSpec = new Ei.Contract.Types.GradeSpec();
        //            gradeSpec.Grade = grade;
        //            gradeSpec.Goals.Add(new Ei.Contract.Types.Goal { RewardType = Ei.RewardType.EggsOfProphecy });
        //            contract.GradeSpecs.Add(gradeSpec);
        //        }
        //        contract.Name = "test";
        //        contract.Identifier = "test";
        //        contract.MaxCoopSize = size;
        //    } else {
        //        contract = (await _db.Contracts.OrderBy(x => x.Created).LastAsync(x => x.ID == contractid)).Details;
        //    }

        //    var users = await _db.DBUsers.Where(x => x.GuildId == GuildId && !x.TempDisabled).ToListAsync();
        //    var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == contractid && x.Created > DateTimeOffset.Now.AddDays(-60)).ToListAsync();
        //    var coopGroups = OrganizeCoops.SortUsersIntoDay1Coops(users, contract, coops, 0);
        //    ViewBag.Contract = contract;
        //    return View(coopGroups);
        //}


        private async Task<(List<PotentialCoopGroup>, Ei.Contract)> _GetGroups(ulong GuildId, string contractid, int skipbg, Guild dbguild, SocketGuild guild, int count) {
            Ei.Contract contract;

            if(string.IsNullOrWhiteSpace(contractid) || contractid == "test") {
                contract = new Ei.Contract();
                foreach(Ei.Contract.Types.PlayerGrade grade in Enum.GetValues(typeof(Ei.Contract.Types.PlayerGrade))) {
                    var gradeSpec = new Ei.Contract.Types.GradeSpec();
                    gradeSpec.Grade = grade;
                    gradeSpec.Goals.Add(new Ei.Contract.Types.Goal { RewardType = Ei.RewardType.EggsOfProphecy });
                    contract.GradeSpecs.Add(gradeSpec);
                }
                contract.Name = "test";
                contract.Identifier = "test";
                contract.MaxCoopSize = 10;
            } else {
                contract = (await _db.Contracts.OrderBy(x => x.Created).LastAsync(x => x.ID == contractid)).Details;
            }

            var users = await _db.DBUsers.Where(x => x.GuildId == GuildId && !x.TempDisabled).ToListAsync();
            var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == contractid && x.Created > DateTimeOffset.Now.AddDays(-60)).ToListAsync();
			var userCsHistoryEntries = await _db.UserCsHistoryEntries.Where(x => x.ContractIdentifier == contract.Identifier).ToListAsync();
			var coopGroups = await OrganizeCoops.SortUsersIntoDay1Coops(users, contract, coops, skipbg, userCsHistoryEntries, dbguild, guild, count);

            return (coopGroups, contract);
        }

        public async Task<IActionResult> Day1CoopsFillLate([FromQuery] ulong GuildId, [FromQuery] string contractid, [FromQuery] int skipbg) {
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == GuildId);
            var guild = _discord.GetGuild(GuildId);
            var t = await _GetGroups(GuildId, contractid, skipbg, dbguild, guild, 0);

            ViewBag.Contract = t.Item2;
            ViewBag.GuildId = GuildId;
            ViewBag.Guild = guild;
            ViewBag.DBGuild = dbguild;
            return View("Day1Coops", t.Item1);
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> ReloadGrade([FromQuery] ulong GuildId, [FromQuery] string ContractID, [FromQuery] string Grade, [FromQuery]int bg, [FromQuery] int count) {
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == GuildId);
            var guild = _discord.GetGuild(GuildId);
            var t = await _GetGroups(GuildId, ContractID, 0, dbguild, guild, count);
            
         
            var boardingGroups = t.Item1.GroupBy(x => x.BoardingGroup).Select(x => new
            {
                BoardingGroup = new { Value = x.Key, Name = dbguild.DisableBG ? guild.GetRole(ulong.Parse(dbguild.GroupRoles.Split(",")[x.Key])).Name : x.Key.ToString() },
                Grades = x.Where(y => (y.PotentialCoops?.Count ?? 0) > 0).Select(y => new {
                    Grade = y.Grade.ToString(),
                    GradeImage = PlayerGradeDetails.GetImage(y.Grade),
                    CoopCount = y.PotentialCoops.Count,
                    Coops = y.PotentialCoops.Select(c => new {
                        Users = c.Users.Select(u =>
                        {
                            var discordUser = _discord.GetGuild(u.User.GuildId).GetUser(u.User.DiscordId);
                            return new {
                                EB = u.Account.Backup.EarningsBonus,
                                EBString = u.Account.Backup.EarningsBonus.ToEggString(),
                                Name = discordUser?.GetCleanName() ?? u.User.DiscordUsername,
                                MultipleAccounts = u.User.EggIncAccounts.Count > 1,
                                AccountName = u.Account.Backup.UserName,
                                FromGroup = (u.Account.Group != x.Key ? u.Account.Group : 0),
                                EggIncId = u.Account.Backup.EggIncId,
                                DatabaseId = u.User.Id
                            };
                        }),
                        TotalEB = c.Users.Sum(u => u.Account.Backup.EarningsBonus).ToEggString()
                    })
                })

            });

            return Content(JsonConvert.SerializeObject(new List<object>()), "application/json");
        }

        public async Task<IActionResult> StartCoops([FromBody] List<CoopStart> coops, [FromQuery]ulong GuildId, [FromQuery] string ContractID, [FromQuery] int bg, [FromQuery] Ei.Contract.Types.PlayerGrade Grade) {
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == GuildId);
            var userIds = coops.SelectMany(x => x.Users.Select(y => y.DatabaseId)).ToList();
            var users = (await _db.DBUsers.Where(x => userIds.Contains(x.Id)).ToListAsync()).SelectMany(x => x.EggIncAccounts.Select(y => new UserByAccount { User = x, Account = y })).ToList();
            var contract = await _db.Contracts.OrderBy(x => x.Created).LastAsync(x => x.ID == ContractID);
            var _words = new Words();
            await Parallel.ForEachAsync(coops, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (coopStart, token) => {
                
                try {
                    var userByAccount = users.Where(x => coopStart.Users.Any(y => y.DatabaseId == x.User.Id && y.EggIncId == x.Account.Id)).ToList();
                    await CreateCoopsV2.Start(userByAccount, contract, Grade, _discord.GetGuild(GuildId), _words, _provider, dbguild);
                } catch(Exception e) {
                    var frame = (new StackTrace(e, true)).GetFrame(0);
                    Console.WriteLine($"⚠️ERROR: {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()}");
                    _bugsnag.Notify(e);
                }

            });

            return await ReloadGrade(GuildId, ContractID, Grade.ToString(), bg, 0);
        }

        public class CoopStart {
            public List<CoopUser> Users { get; set; }
        }
        public class CoopUser {
            public string EggIncId { get; set; }
            public Guid DatabaseId { get; set; }
        }
        //[Authorize(Roles = "Admin,GuildAdmin")]
        //public async Task<IActionResult> CreateDay1Coops([FromQuery] ulong GuildId, [FromQuery] string contractid, [FromQuery] int skipbg) {
        //    Console.WriteLine("Gettings Users");
        //    var contract = await _db.Contracts.OrderBy(x => x.Created).LastAsync(x => x.ID == contractid);

        //    var users = await _db.DBUsers.Where(x => x.GuildId == GuildId && !x.TempDisabled).ToListAsync();
        //    Console.WriteLine("Gettings Coops");
        //    var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == contractid && x.Created > DateTimeOffset.Now.AddDays(-60)).ToListAsync();
        //    Console.WriteLine("Sorting");
        //    var coopGroups = await OrganizeCoops.SortUsersIntoDay1Coops(users, contract.Details, coops, skipbg);
        //    ViewBag.Contract = contract;

        //    var coopsCreated = 0;
        //    var _words = new Words();
        //    var dbguild = await _db.Guilds.FirstAsync(x => x.Id == GuildId);
        //    foreach(var group in coopGroups.Where(x => x.bg == (skipbg + 1).ToString())) {
        //        Console.WriteLine($"BG {group.bg}, Grade {group.Grade}, Count {group.PotentialCoops.Count(x => x.Users.Count > 2)}");
        //        var coopsToCreate = group.PotentialCoops.Where(x => x.Users.Count > 2);

        //        await Parallel.ForEachAsync(coopsToCreate, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (coop, token) => {
        //            try {
        //                await CreateCoopsV2.Start(coop.Users, contract, group.Grade, _discord.GetGuild(GuildId), _words, _provider, dbguild);
        //            } catch(Exception e) {
        //                var frame = (new StackTrace(e, true)).GetFrame(0);
        //                Console.WriteLine($"⚠️ERROR: {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()}");
        //                _bugsnag.Notify(e);
        //            }

        //        });
        //        coopsCreated += coopsToCreate.Count();
        //        await _db.SaveChangesAsync();
        //    }

        //    return Content($"Success {coopsCreated} coops created");
        //}

        public async Task<IActionResult> Details([FromQuery] ulong GuildId, [FromQuery] String ContractID, [FromQuery] UInt32 League) {
            if(User.IsInRole("Admin") || User.IsInRole("GuildAdmin") || true) {
                await _discord.Guilds.First(x => x.Id == GuildId).DownloadUsersAsync();

                var guildContract = await _db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == ContractID && x.GuildID == GuildId);


                var coopsBreakdown = await Prefarm.GetBreakdown(_db, guildContract, _discord, League);

                ViewBag.Discord = _discord;

                //var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == ContractID && x.Created > DateTimeOffset.Now.AddDays(-60)).ToListAsync();

                return View(new CoopsViewModel {
                    GuildContract = guildContract,
                    CoopsBreakdown = coopsBreakdown,
                    League = League
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
        public async Task<IActionResult> StartCoop([FromBody] List<UserPreFarm> Users, [FromQuery] ulong GuildId, [FromQuery] String ContractID, [FromQuery] Ei.Contract.Types.PlayerGrade grade) {
            var contract = await _db.Contracts.FirstAsync(x => x.ID == ContractID);
            var guild = _discord.GetGuild(GuildId);

            var eggIncIDs = Users.Select(x => x.EggIncId);

            var dbUserIds = Users.Select(x => x.DatabaseId).ToList();
            var dbusers = await _db.DBUsers.Where(x => dbUserIds.Contains(x.Id)).ToListAsync();
            var userswithbackups = dbusers.SelectMany(x => x.EggIncAccounts.Select(y => new UserByAccount { User = x, Account = y }));
            var userdetails = Users.Select(x => {
                var user = userswithbackups.FirstOrDefault(y => y.Account.Backup.EggIncId == x.EggIncId);
                var account = user.User.EggIncAccounts.First(y => y.Id == x.EggIncId);
                return new UserByAccount {
                    Account = account,
                     User = user.User
                };
            }).ToList();
            //(guildContract.Contract, userswithbackups.First(y => y.Backup.EggIncId == x.EggIncId && y.User.Id == x.DatabaseId), _discord, League)).ToList();
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == GuildId);
            var coop = await CreateCoopsV2.Start(userdetails, contract, grade, guild, new Words(), _provider, dbguild);
            await _db.SaveChangesAsync();

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
            var eggIncName = dbuser.EggIncAccounts.First(x => x.Id == EggIncId).Name;
            var xref = await CreateCoopsV2.MoveUser(targetCoop, UserId, EggIncId, eggIncName, discordUser, dbuser, channel, null);

            if(xref == null) {
                return Json(new { error = $"Unable to add permissions for {dbuser.DiscordUsername}, likely not in overflow server" });
            }

            _db.Add(xref);
            await _db.SaveChangesAsync();

            var guildContract = await _db.GuildContracts.AsQueryable().FirstOrDefaultAsync(x => x.ContractID == targetCoop.ContractID && x.GuildID == guild.Id && x.League == targetCoop.League);

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
            public UInt32 League { get; set; }
        }
    }
}
