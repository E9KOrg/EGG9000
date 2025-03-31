using Discord.WebSocket;
using EGG9000.Bot;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Controllers {
    [Authorize]
    public class ContractController(ApplicationDbContext _db, DiscordSocketClient _discord, Bugsnag.IClient _bugsnag, IServiceProvider _provider, ILogger<ContractController> _logger) : Controller {
        public async Task<IActionResult> Index() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var contracts = await _db.GuildContracts.Include(x => x.Contract).Where(x => x.GuildID == guildId && x.Contract.Created > DateTimeOffset.Now.AddMonths(-2)).OrderByDescending(x => x.Contract.Created).ToListAsync();
            var dbguild = await _db.Guilds.FindAsync(guildId);
            return View((contracts, dbguild));
        }

        public IActionResult Coop() {
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


        private async Task<((List<PotentialCoopGroup> coopGroups, List<(string reason, UserByAccount account)> excluded), Contract)> _GetGroups(ulong GuildId, string contractid, int skipbg, Guild dbguild, SocketGuild guild, int count) {
            Contract contract;

            if(string.IsNullOrWhiteSpace(contractid) || contractid == "test") {
                contract = new Contract();
                var eicontract = new Ei.Contract();
                foreach(Ei.Contract.Types.PlayerGrade grade in Enum.GetValues(typeof(Ei.Contract.Types.PlayerGrade))) {
                    var gradeSpec = new Ei.Contract.Types.GradeSpec {
                        Grade = grade
                    };
                    gradeSpec.Goals.Add(new Ei.Contract.Types.Goal { RewardType = Ei.RewardType.EggsOfProphecy });
                    eicontract.GradeSpecs.Add(gradeSpec);
                }
                eicontract.Name = "test";
                eicontract.Identifier = "test";
                eicontract.MaxCoopSize = 10;
                eicontract.Egg = Ei.Egg.Edible;
                contract.OverwriteDetails(eicontract);
            } else if(contractid == "sub") {
                contract = new Contract();
                var eicontract = new Ei.Contract();
                foreach(Ei.Contract.Types.PlayerGrade grade in Enum.GetValues(typeof(Ei.Contract.Types.PlayerGrade))) {
                    var gradeSpec = new Ei.Contract.Types.GradeSpec {
                        Grade = grade
                    };
                    gradeSpec.Goals.Add(new Ei.Contract.Types.Goal { RewardType = Ei.RewardType.EggsOfProphecy });
                    eicontract.GradeSpecs.Add(gradeSpec);
                }
                eicontract.Name = "test";
                eicontract.Identifier = "test";
                eicontract.MaxCoopSize = 10;
                eicontract.Egg = Ei.Egg.Edible;
                eicontract.CcOnly = true;
                contract.OverwriteDetails(eicontract);
            } else {
                contract = (await _db.Contracts.OrderBy(x => x.Created).LastAsync(x => x.ID == contractid));
            }

            var users = await _db.DBUsers.Where(x => x.GuildId == GuildId && !x.TempDisabled).ToListAsync();
            var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == contractid && x.Created > DateTimeOffset.Now.AddDays(-60)).ToListAsync();
            var userCsHistoryEntries = await _db.UserCsHistoryEntries.Where(x => x.ContractIdentifier == contract.ID).ToListAsync();
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
        public async Task<IActionResult> ReloadGrade([FromQuery] ulong GuildId, [FromQuery] string ContractID, [FromQuery] string Grade, [FromQuery] int bg, [FromQuery] int count) {
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == GuildId);
            var guild = _discord.GetGuild(GuildId);
            var t = await _GetGroups(GuildId, ContractID, 0, dbguild, guild, count);

            var boardingGroups = t.Item1.coopGroups.GroupBy(x => x.BoardingGroup).Select(x => new {
                BoardingGroup = new { Value = x.Key, Name = dbguild.DisableBG ? guild.GetRole(ulong.Parse(dbguild.GroupRoles.Split(",")[x.Key])).Name : x.Key.ToString() },
                Grades = x.Where(y => (y.PotentialCoops?.Count ?? 0) > 0).Select(y => new {
                    Grade = y.Grade.ToString(),
                    GradeImage = PlayerGradeDetails.GetImage(y.Grade),
                    CoopCount = y.PotentialCoops.Count,
                    Coops = y.PotentialCoops.Select(c => new {
                        Users = c.Users.Select(u => {
                            var discordUser = _discord.GetGuild(u.User.GuildId).GetUser(u.User.DiscordId);
                            return new {
                                EB = u.Account.Backup.EarningsBonus,
                                EBString = u.Account.Backup.EarningsBonus.ToEggString(),
                                Name = discordUser?.GetCleanName() ?? u.User.DiscordUsername,
                                MultipleAccounts = u.User.EggIncAccounts.Count > 1,
                                AccountName = u.Account.Backup.UserName,
                                FromGroup = (u.Account.Group != x.Key ? u.Account.Group : 0),
                                EggIncId = u.Account.Backup.EggIncId,
                                DatabaseId = u.User.Id,
                                Group = u.Account.Group
                            };
                        }),
                        TotalEB = c.Users.Sum(u => u.Account.Backup.EarningsBonus).ToEggString()
                    })
                })

            });

            var gradeGroup = boardingGroups.FirstOrDefault(x => x.BoardingGroup.Value == bg)?.Grades.FirstOrDefault(x => x.Grade == Grade);
            return Content(gradeGroup == null ? "[]" : JsonConvert.SerializeObject(gradeGroup.Coops), "application/json");
        }

        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> StartCoops([FromBody] List<CoopStart> coops, [FromQuery] ulong GuildId, [FromQuery] string ContractID, [FromQuery] int bg, [FromQuery] Ei.Contract.Types.PlayerGrade Grade) {
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == GuildId);
            var userIds = coops.SelectMany(x => x.Users.Select(y => y.DatabaseId)).ToList();
            var useRoles = dbguild.DisableBG;
            var roles = useRoles ? dbguild.GroupRoles.Split(",") : Array.Empty<string>();
            var users = (await _db.DBUsers.Where(x => userIds.Contains(x.Id)).ToListAsync()).SelectMany(x => x.EggIncAccounts.Select(y => new UserByAccount {
                User = x,
                Account = y
            })).ToList();
            var contract = await _db.Contracts.OrderBy(x => x.Created).LastAsync(x => x.ID == ContractID);
            var _words = new Words();

            await Parallel.ForEachAsync(coops, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (coopStart, token) => {

                try {

                    var userByAccount = new List<UserByAccount>();
                    foreach(var coopUser in coopStart.Users) {
                        var user = users.FirstOrDefault(x => coopUser.DatabaseId == x.User.Id && coopUser.EggIncId == x.Account.Id);
                        user.Group = useRoles ? ulong.Parse(roles[coopUser.Group]) : coopUser.Group;
                        userByAccount.Add(user);
                    }
                    await CreateCoopsV2.Start(userByAccount, contract, Grade, _discord.GetGuild(GuildId), _words, _provider, dbguild, (uint)bg, contract.cc_only);
                } catch(Exception e) {
                    var frame = new StackTrace(e, true).GetFrame(0);
                    Console.WriteLine($"⚠️ERROR: {e}  {frame.GetFileName()} {frame.GetFileLineNumber()}");
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
            public uint Group { get; set; }
        }

        public async Task<IActionResult> Details([FromQuery] ulong GuildId, [FromQuery] string ContractID, [FromQuery] uint League) {
            if(User.IsInRole("Admin") || User.IsInRole("GuildAdmin") || true) {
                await _discord.Guilds.First(x => x.Id == GuildId).DownloadUsersAsync();

                var guildContract = await _db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == ContractID && x.GuildID == GuildId);


                var coopsBreakdown = await GetBreakdown(_db, guildContract, _discord, League);

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

        public async Task<IActionResult> ScoreGraph([FromQuery] ulong GuildId, [FromQuery] string ContractID) {
            var contract = await _db.Contracts.AsQueryable().FirstOrDefaultAsync(x => x.ID == ContractID);
            ViewBag.Contract = contract;
            var claimsIdentity = (ClaimsIdentity)User.Identity;
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

        //[Authorize(Roles = "Admin,GuildAdmin")]
        //public async Task<IActionResult> StartCoop([FromBody] List<UserPreFarm> Users, [FromQuery] ulong GuildId, [FromQuery] String ContractID, [FromQuery] Ei.Contract.Types.PlayerGrade grade, [FromQuery]uint bg) {
        //    var contract = await _db.Contracts.FirstAsync(x => x.ID == ContractID);
        //    var guild = _discord.GetGuild(GuildId);

        //    var eggIncIDs = Users.Select(x => x.EggIncId);

        //    var dbguild = await _db.Guilds.FirstAsync(x => x.Id == GuildId);
        //    var useRoles = dbguild.DisableBG;
        //    var roles = useRoles ? dbguild.GroupRoles.Split(",") : new string[0];

        //    var dbUserIds = Users.Select(x => x.DatabaseId).ToList();
        //    var dbusers = await _db.DBUsers.Where(x => dbUserIds.Contains(x.Id)).ToListAsync();
        //    var userswithbackups = dbusers.SelectMany(x => x.EggIncAccounts.Select(y => new UserByAccount { User = x, Account = y }));
        //    var userdetails = Users.Select(x => {
        //        var user = userswithbackups.FirstOrDefault(y => y.Account.Backup.EggIncId == x.EggIncId);
        //        var account = user.User.EggIncAccounts.First(y => y.Id == x.EggIncId);
        //        return new UserByAccount {
        //            Account = account,
        //             User = user.User, 
        //            Group = useRoles ? uint.Parse(roles[x.Group]) : x.Group
        //    };
        //    }).ToList();
        //    //(guildContract.Contract, userswithbackups.First(y => y.Backup.EggIncId == x.EggIncId && y.User.Id == x.DatabaseId), _discord, League)).ToList();
        //    var coop = await CreateCoopsV2.Start(userdetails, contract, grade, guild, new Words(), _provider, dbguild);
        //    await _db.SaveChangesAsync();

        //    return Json(new { coopName = coop.Name });
        //}


        [Authorize(Roles = "Admin,GuildAdmin")]
        public async Task<IActionResult> MoveToCoop([FromQuery] Guid CoopId, [FromQuery] Guid UserId, [FromQuery] string EggIncId) {
            var targetCoop = await _db.Coops.Include(x => x.Contract).AsQueryable().FirstAsync(x => x.Id == CoopId);
            var dbuser = await _db.DBUsers.AsQueryable().FirstAsync(x => x.Id == UserId);

            var existingXref = await _db.UserCoopXrefs.AsQueryable().FirstOrDefaultAsync(x => x.Coop.Created > DateTimeOffset.Now.AddMonths(-6) && x.Coop.ContractID == targetCoop.ContractID && x.EggIncId == EggIncId && x.Coop.Status != CoopStatusEnum.Failed);
            if(existingXref != null) {
                return Json(new { error = $"{dbuser.DiscordUsername} has already been assigned a co-op." });
            }

            var guild = _discord.GetGuild(targetCoop.GuildId);
            var discordUser = guild.Users.First(x => x.Id == dbuser.DiscordId);
            var guildId = targetCoop.OverflowGuildId > 0 ? targetCoop.OverflowGuildId : targetCoop.GuildId;

            var channel = targetCoop.ThreadID != 0 ? (SocketThreadChannel)_discord.GetChannel(targetCoop.ThreadID) : (SocketTextChannel)_discord.GetChannel(targetCoop.DiscordChannelId);
            var eggIncName = dbuser.EggIncAccounts.First(x => x.Id == EggIncId).Name;
            var xref = await CreateCoopsV2.MoveUser(targetCoop, UserId, EggIncId, eggIncName, _db, discordUser, dbuser, channel, null);

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
            public uint League { get; set; }
        }

        public async Task<IActionResult> RecentScoresGrid([FromQuery] ulong guildid = default) {
            _logger.LogInformation("Test");
            var times = new TimingsFactory(_logger).Start();
            if(guildid == default) {
                guildid = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            }

            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guildid);
            times.Set("Get Guild");
            var contracts = await _db.Contracts.OrderByDescending(x => x.Created).Take(10).ToListAsync();
            times.Set("Get Contracts");
            var contractIDs = contracts.Select(x => x.ID).ToArray();
            var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => contractIDs.Contains(x.ContractID) && x.Status == CoopStatusEnum.Completed && x.GuildId == guildid).ToListAsync();
            times.Set("Get Coops");
            var userids = coops.SelectMany(x => x.UserCoopsXrefs).Select(x => x.UserId).Distinct().ToArray();
            var users = await _db.DBUsers.Where(x => userids.Contains(x.Id)).ToListAsync();
            times.Set("Get Users");

            if(dbguild.DisableBG && !dbguild.GroupRoles.Any()) {
                _logger.LogError("Boarding group disabled, and group roles not found. Unable to continue peacefully.");
                return View();
            }

            var groupRoles = dbguild.DisableBG ? dbguild.GroupRoles?.Split(",").Select(ulong.Parse).ToArray() : [];

            var customEggs = await _db.GetCustomEggsAsync();

            var guild = _discord.GetGuild(guildid);
            await guild.DownloadUsersAsync();
            times.Set("Download Guild Users");
            var scoreGridItems = coops.SelectMany(coop => {
                var contract = contracts.First(x => x.ID == coop.ContractID);
                var details = new CoopDetails(coop, contract, coop.League, coop.UserCoopsXrefs.SelectMany(xref => {
                    var user = users.First(u => u.Id == xref.UserId);
                    return user.EggIncAccounts.Select(acc => new UserWithBackup { Account = acc, Backup = acc.Backup, User = xref.User });
                }).ToList(), customEggs, _discord, coop.LastStatusUpdate);

                return details.CoopParticipants.Where(p => p.DBUser is not null && p.DBUser.GuildId == guildid).Select(p => {
                    var role = groupRoles.Length > 0 ? dbguild.GroupRoles.Split(",").FirstOrDefault(gr => guild.GetUser(p.DBUser.DiscordId)?.Roles.Any(r => r.Id.ToString() == gr) ?? false) : "";
                    return new ScoreGridItem {
                        Name = p.Name,
                        RoleId = role,
                        ContractId = contract.ID,
                        Score = p.Projected
                    };
                });
            }).ToArray();
            times.Set("Setup Grid Items");
            var roles = dbguild.DisableBG ? dbguild.GroupRoles.Split(",").Select(x => guild.GetRole(ulong.Parse(x))).ToArray() : [null];

            var scoreGridContracts = contracts.Where(x => scoreGridItems.Any(y => y.ContractId == x.ID)).OrderByDescending(x => x.Created).Select(x => new ScoreGridContract {
                ContractId = x.ID.ToString(),
                ContractName = x.Name
            }).ToArray();
            times.Finished();
            return View((scoreGridContracts, scoreGridItems, roles));
        }

        public record ScoreGridItem {
            public string ContractId { get; set; }
            public double Score { get; set; }
            public string Name { get; set; }
            public string RoleId { get; set; }
        }

        public record ScoreGridContract {
            public string ContractId { get; set; }
            public string ContractName { get; set; }
        }
    }
}
