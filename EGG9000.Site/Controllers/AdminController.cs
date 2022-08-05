using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Policy;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Discord;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using Newtonsoft.Json;

namespace EGG9000.Site.Controllers {
    [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
    public class AdminController : Controller {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly DiscordSocketClient _discord;
        private readonly IMemoryCache _cache;

        public AdminController(
            UserManager<IdentityUser> userManager,
            DiscordSocketClient discord,
            ApplicationDbContext db, IMemoryCache cache) {
            _db = db;
            _userManager = userManager;
            _discord = discord;
            _cache = cache;
        }

        public class PrestigeGain {
            public UserSnapShot SnapShot { get; set; }
            public DBUser User { get; set; }
            public ulong gain { get; set; }
        }

        public async Task<IActionResult> TestKick() {
            //var user = await _db.DBUsers.FirstAsync(x => x.DiscordId == 248865520756064257);

            //var id = user.Backups.First().EggIncId;

            //var r = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
            //    ClientVersion = ContractsAPI.ClientVersion,
            //    ContractIdentifier = "spring-break-2022",
            //    CoopIdentifier = "earlydancer51",
            //    PlayerIdentifier = "EI6427300328898560",
            //    Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
            //    RequestingUserId = "EI6427300328898560"
            //}, "EI6427300328898560");
            //return Content(r.ToString());


            var wrongcoopcode = "ialwayswin";
            var contractID = "easter-rush-2022";
            var DiscordUserID = (ulong)804144041284993064;
            var db = _db;
            //var targetCoop = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).AsQueryable().FirstAsync(x => x.DiscordChannelId == 962019257309335652);
            //if(targetCoop == null) {
            //    return Content($"⚠️ERROR: Command only works in co-op channels");
            //}

            //if(wrongcoopcode.Equals(targetCoop.Name, StringComparison.OrdinalIgnoreCase)) {
            //    return Content($"⚠️ERROR: Unable to leave currently assigned co-op");
            //}



            var coopStatus = await ContractsAPI.GetCoopStatus(contractID, wrongcoopcode.ToLower().Trim());
            if(coopStatus is null) {
                //await command.ModifyOriginalResponseAsync(m => m.Content = $"⚠️ERROR: Unable to find co-op {wrongcoopcode}");
                //return;
                return Content("1");
            }

            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == DiscordUserID);

            var egginids = user.EggIncIds.Select(x => x.Id).ToList();

            var participant = coopStatus.Participants.FirstOrDefault(x => egginids.Contains(x.UserId));
            if(participant is null) {
                //await command.ModifyOriginalResponseAsync(m => m.Content = $"Unable to find an assigned user in co-op {wrongcoopcode}. {(coopStatus.Participants.Count > 0 ? $"Users found: \n{string.Join("\n", coopStatus.Participants.Select(x => x.UserName))}" : "")}");
                //return;
                return Content("2");
            }

            if(coopStatus.Public) {
                var r2 = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                    ClientVersion = ContractsAPI.ClientVersion,
                    ContractIdentifier = contractID,
                    CoopIdentifier = wrongcoopcode,
                    Public = false,
                    RequestingUserId = coopStatus.CreatorId
                }, coopStatus.CreatorId);
            }

            var r = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = contractID,
                CoopIdentifier = wrongcoopcode,
                PlayerIdentifier = participant.UserId,
                Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
                RequestingUserId = coopStatus.CreatorId
            }, coopStatus.CreatorId);

            if(coopStatus.Public)
                await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                    ClientVersion = ContractsAPI.ClientVersion,
                    ContractIdentifier = contractID,
                    CoopIdentifier = wrongcoopcode,
                    Public = true,
                    RequestingUserId = coopStatus.CreatorId
                }, coopStatus.CreatorId);

            if(!r) {
                //await command.ModifyOriginalResponseAsync(m => m.Content = $"⚠️ERROR: Unable to remove user from co-op {wrongcoopcode}");
                //return;
                return Content(coopStatus.Public ? "4" : "3");
            }
            return Content("Success");
        }

        public async Task<IActionResult> CheckForDuplicateXrefs() {
            var xrefs = await _db.UserCoopXrefs.Where(x => x.Coop.ContractID == "diamonds-2022").ToListAsync();

            var groups = xrefs.GroupBy(x => x.EggIncId);

            foreach(var group in groups.Where(x => x.Count() > 1)) {
                var user = await _db.DBUsers.FirstAsync(x => x.Id == group.First().UserId);
                Console.WriteLine($"Duplicate xrefs for {user.DiscordUsername}");
            }
            return Content("");
        }


        public async Task<IActionResult> LookForLargeJump() {
            var snapshots = await _db.UserSnapShots.ToListAsync();

            var sgroups = snapshots.GroupBy(x => x.UserId);

            var gains = new List<PrestigeGain>();
            List<DBUser> users = new List<DBUser>();
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
                                gains.Add(new PrestigeGain { SnapShot = osnap, User = user, gain = osnap.Prestiges - previousPrestiges });
                            }
                            //Console.WriteLine($"{user.DiscordUsername} on {osnap.Date.ToShortDateString()} jumped by {osnap.Prestiges - previousPrestiges} prestiges");
                        }
                        previousPrestiges = osnap.Prestiges;
                    }
                }
            }

            foreach(var gain in gains.OrderByDescending(x => x.gain).Take(25)) {
                Console.WriteLine($"{gain.User.DiscordUsername} on {gain.SnapShot.Date.ToShortDateString()} jumped by {gain.gain} prestiges, {gain.SnapShot.EggIncID}");
            }

            return Content("Success");
        }

        //public async Task<IActionResult> TestSQL() {
        //    var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);

        //    var userCoopStats = _db.UserCoopXrefs.Where(x => x.JoinedCoop && x.Coop.GuildId == guildId).GroupBy(x => x.EggIncId).Select(x => new {
        //        Start = x.OrderBy(y => y.CreatedOn).First().CreatedOn,
        //        End = x.OrderByDescending(y => y.CreatedOn).First().CreatedOn
        //    });


        //    //var test = await userCoopStats.ToListAsync();

        //    //Console.WriteLine(test.Count);
        //    return Content(userCoopStats.ToQueryString());
        //}

        public async Task<IActionResult> Index() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);


            Dictionary<DateTimeOffset, int[]> days;
            var adminDaysCacheKey = $"AdminDays{guildId}";
            if(!_cache.TryGetValue(adminDaysCacheKey, out days)) {

                var coops = await _db.Coops.AsQueryable().Select(x => new { x.Created, Finished = x.CoopCompleted ?? x.CoopEnds }).ToListAsync();

                days = new Dictionary<DateTimeOffset, int[]>();
                coops = coops.Where(x => x.Created != DateTimeOffset.MinValue).ToList();

                var xrefs = await _db.UserCoopXrefs.Where(x => x.JoinedCoop && x.Coop.GuildId == guildId).Select(x => new { x.EggIncId, x.CreatedOn }).OrderBy(x => x.CreatedOn).ToListAsync();
                var eggIncIdGroups = xrefs.GroupBy(x => x.EggIncId).Select(x => new {
                    Start = x.First().CreatedOn,
                    End = x.Last().CreatedOn
                });

                for(var start = coops.OrderBy(x => x.Created).First().Created.Date; start <= DateTimeOffset.Now; start = start.AddDays(1)) {
                    var count = coops.Count(c => c.Created.Date <= start && (c.Finished?.Date ?? c.Created.AddDays(4).Date) >= start);
                    var accountsCount = eggIncIdGroups.Count(x => x.Start < start && x.End > start.AddDays(-14));
                    days.Add(start, new[] { count, accountsCount });
                }
                _cache.Set(adminDaysCacheKey, days, TimeSpan.FromDays(1));
            }
            var guildContractsToScore = await _db.GuildContracts.Include(x => x.Contract).AsQueryable().Where(x => x.Contract.MaxUsers > 1 && x.GuildID == 656455567858073601 && x.Created > DateTimeOffset.Now.AddMonths(-3)).ToListAsync();
            var contractsToScore = guildContractsToScore.GroupBy(x => x.ContractID).Where(x => x.All(y => y.DeletedChannel && !y.HasScores)).Select(x => x.First().Contract).ToList();


            return View(new IndexViewModel {
                Contracts = await _db.Contracts.AsQueryable().OrderByDescending(x => x.Created).Take(10).ToListAsync(),
                Guilds = _discord.Guilds.Where(x => x.Id == guildId || guild.OverflowServers.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new GuildDetails {
                    Name = x.Name,
                    ChannelCount = x.Channels.Count,
                    ActiveCoops = x.TextChannels.Where(c => c.Category != null).Count(c => c.Category.Name.Contains("coops") && !c.Category.Name.Contains("finished")),
                    FinishedCoops = x.TextChannels.Where(c => c.Category != null).Count(c => c.Category.Name.Contains("coops") && c.Category.Name.Contains("finished")),
                }).ToList(),
                Days = days,
                ContractsToScore = contractsToScore
            });
        }

        public class IndexViewModel {
            public List<Contract> Contracts { get; set; }
            public List<GuildDetails> Guilds { get; set; }
            public Dictionary<DateTimeOffset, int[]> Days { get; set; }
            public List<Contract> ContractsToScore { get; set; }
        }

        public class GuildDetails {
            public string Name { get; set; }
            public int ChannelCount { get; set; }
            public int ActiveCoops { get; set; }
            public int FinishedCoops { get; set; }
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> EventCustomization() {
            return View(await _db.EventCustomizations.AsQueryable().OrderByDescending(x => x.Priority).ToListAsync());
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SaveEventCustomization([FromBody] EventCustomization eventCustomization) {
            _db.Entry(eventCustomization).State = EntityState.Modified;
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
            public List<IdentityUser> Users { get; set; }
            public List<IdentityUserLogin<string>> Logins { get; set; }
            public List<IdentityUserRole<string>> UserRoles { get; set; }
            public List<IdentityRole> Roles { get; set; }
            public List<DBUser> DbUsers { get; set; }
        }

        public async Task<IActionResult> Contract([FromQuery] string contractid, [FromQuery] bool all = false) {
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

            //await _db.Users.AsQueryable().Where(x => (x.GuildId == user.GuildId || all) && x._LastBackup != null).ToListAsync()
            return View(dbusers.Where(x => x.Backups != null).ToList());
        }

        public async Task<IActionResult> ContractScores([FromQuery] string contractid, [FromQuery] bool all = false) {
            ViewBag.ContractID = contractid;
            ViewBag.Guilds = await _db.Guilds.AsQueryable().ToListAsync();
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));


            var coops = await _db.Coops.AsQueryable().Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.GuildId == user.GuildId && x.ContractID == contractid).ToListAsync();
            var contract = await _db.Contracts.FirstAsync(x => x.ID == contractid);

            var scores = ContractScoring.GetContractScores(coops, contract);

            return View(scores);
        }

        public static double scoreThreshold = 5e-3;
        public async Task<IActionResult> Slackers() {
            var loginuser = (await _userManager.GetUserAsync(User));
            var logins = await _userManager.GetLoginsAsync(loginuser);
            var user = await _db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));

            var slackers = await _db.DBUsers.AsQueryable().Include(x => x.UserCoopXrefs).Where(x => x.GuildId == user.GuildId && x.UserCoopXrefs.Any(y => y.RunningScore < scoreThreshold)).Select(x => new Slacker {
                DiscordUsername = x.DiscordUsername,
                UserCoopXrefs = x.UserCoopXrefs.Select(y => new SlackerXref {
                    Score = y.Score,
                    ContractID = y.Coop.ContractID,
                    RunningScore = y.RunningScore,
                    Date = y.Coop.CoopCompleted ?? y.Coop.CoopEnds ?? y.CreatedOn
                })
            }).ToListAsync();

            slackers = slackers.Where(x => x.UserCoopXrefs.Any(y => y.RunningScore < scoreThreshold && y.Date > DateTimeOffset.Now.AddMonths(-4))).ToList();


            ViewBag.Contracts = await _db.Contracts.AsQueryable().Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6)).ToListAsync();

            return View(slackers);
        }

        public class Slacker {
            public string DiscordUsername { get; set; }
            public IEnumerable<SlackerXref> UserCoopXrefs { get; set; }
        }

        public class SlackerXref {
            public float? Score { get; set; }
            public string ContractID { get; set; }
            public float? RunningScore { get; set; }
            public DateTimeOffset Date { get; set; }
        }


        public async Task<IActionResult> DeleteOutsideCoopMessage() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var coops = await _db.Coops.AsQueryable().Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == guildId && !x.DeletedChannel).ToListAsync();

            var coopsToFix = coops.Where(x => x.UserCoopsXrefs.Any(y => y.OutsideCoop));

            foreach(var coop in coopsToFix) {
                var channel = _discord.Guilds.First(x => x.Id == coop.OverflowGuildId).GetTextChannel(coop.DiscordChannelId);
                var messages = await channel.GetMessagesAsync().FlattenAsync();
                foreach(var message in messages.Where(x => x.Content.Contains("has joined another co-op named . Please use the command"))) {
                    Console.WriteLine($"Deleting message from {coop.Name}");
                    await message.DeleteAsync();
                }

            }

            return Content("Success");
        }
        public async Task<IActionResult> CalculateScore([FromQuery] string contractid) {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);

            var guildContracts = await _db.GuildContracts.Include(x => x.Contract).AsQueryable().Where(x => x.ContractID == contractid && x.GuildID == guildId).ToListAsync();
            var coops = await _db.Coops.AsQueryable().Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == guildId && x.Created > DateTimeOffset.Now.AddMonths(-6)).ToListAsync();

            var contractCoops = coops.Where(x => x.ContractID == contractid).ToList();
            Console.WriteLine($"Processing {contractid}");
            var contract = await _db.Contracts.FirstAsync(x => x.ID == contractid);
            var scores = ContractScoring.GetContractScores(contractCoops, contract);
            foreach(var score in scores) {
                score.xref.Score = score.Score;
                score.xref.SoulPower = score.SoulPower;
            }
            guildContracts.Where(x => x.ContractID == contractid).ToList().ForEach(x => x.HasScores = true);
            await _db.SaveChangesAsync();

            var userXrefs = coops.SelectMany(x => x.UserCoopsXrefs).Where(x => x.JoinedCoop).GroupBy(x => x.UserId);

            foreach(var userXref in userXrefs) {
                var xrefs = userXref.OrderByDescending(x => x.CreatedOn).ToList();
                foreach(var xref in xrefs.Where(x => x.Coop.ContractID == contractid)) {
                    //if(xref.RunningScore == null) {
                    var lastFourXrefs = xrefs.Where(x => x.CreatedOn <= xref.CreatedOn && x.Score.HasValue).Take(4).ToList();
                    if(lastFourXrefs.Count == 4 && lastFourXrefs.All(x => x.Score.HasValue)) {
                        xref.RunningScore = lastFourXrefs.Average(x => x.Score);
                    } else {
                        xref.RunningScore = null;
                    }
                    //}
                }
            }
            await _db.SaveChangesAsync();

            var users = _db.DBUsers.Where(x => x.GuildId == guildId).Select(x => new {
                x.Id,
                x.DiscordId,
                x.DiscordUsername,
                x._eggIncIds
            });
            var xrefsBelowThreshold = userXrefs.SelectMany(x => x.Where(y => y.Coop.ContractID == contractid && y.RunningScore.HasValue && y.RunningScore < 1e-3).Select(y => {
                var user = users.FirstOrDefault(u => u.Id == y.UserId);
                if(user == null) {
                    return null;
                }


                return new ScoreUser {
                    DiscordId = user.DiscordId,
                    DiscordUsername = user.DiscordUsername,
                    RunningScore = y.RunningScore.Value
                };
            }));



            var guild = _discord.GetGuild(guildId);
            var beastModeRole = guild.GetRole(938563459812049008);

            var topXrefs = userXrefs.SelectMany(x => x.Where(y => y.Coop.ContractID == contractid && y.Score.HasValue).Select((y => {
                var user = users.FirstOrDefault(u => u.Id == y.UserId && !u._eggIncIds.Contains("},{"));
                if(user == null) {
                    return null;
                }

                var discordUser = guild.GetUser(user.DiscordId);


                return new ScoreUser {
                    DiscordId = user.DiscordId,
                    DiscordUsername = user.DiscordUsername,
                    Score = y.Score.Value,
                    DiscordUser = discordUser,
                };
            }))).Where(x => x != null).OrderByDescending(x => x.Score).Take(10);




            foreach(var topxref in topXrefs) {
                if(topxref.DiscordUser == null)
                    topxref.DiscordUser = await _discord.Rest.GetGuildUserAsync(guildId, topxref.DiscordId);
                var tempRole = await _db.TemporaryRoles.FirstOrDefaultAsync(x => x.RoleId == beastModeRole.Id && topxref.DiscordId == x.UserId && x.Expires > DateTimeOffset.Now);
                if(tempRole == null) {
                    tempRole = new TemporaryRole { RoleId = beastModeRole.Id, Created = DateTimeOffset.Now, UserId = topxref.DiscordId, GuildId = guildId };
                    _db.Add(tempRole);
                    await topxref.DiscordUser.AddRoleAsync(beastModeRole);
                    Console.WriteLine($"Role added to {topxref.DiscordUser.Nickname}");
                    await Task.Delay(600);
                }
                tempRole.Reason = $"{beastModeRole.Name} awarded for {guildContracts.First().Contract.Name}";
                tempRole.Expires = DateTimeOffset.Now.AddDays(7);

            }

            await _db.SaveChangesAsync();

            var mentions = topXrefs.Select(x => $"{Math.Round(x.Score)} <@{x.DiscordId}>");

            await guild.GetTextChannel(656455568353132546).SendMessageAsync($"Added the role {beastModeRole.Emoji} {beastModeRole.Name} to the following users until <t:{DateTimeOffset.Now.AddDays(7).ToUnixTimeSeconds()}:f> for the contract {guildContracts.First().Contract.Name} \n{string.Join("\n", mentions)}");


            return View(new ScoreResult {
                UsersBelowThreshold = xrefsBelowThreshold.Where(x => x != null).OrderBy(x => x.DiscordUsername).ToList(),
                TopScore = topXrefs.ToList()

            });
        }
        public async Task<IActionResult> ReCalculateRunningScore() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);

            //var guildContracts = await _db.GuildContracts.Include(x => x.Contract).AsQueryable().Where(x => x.GuildID == guildId).ToListAsync();
            var coops = await _db.Coops.AsQueryable().Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == guildId && x.Created > DateTimeOffset.Now.AddMonths(-6)).ToListAsync();

            //var contractCoops = coops.Where(x => x.ContractID == contractid).ToList();

            var userXrefs = coops.SelectMany(x => x.UserCoopsXrefs).Where(x => x.JoinedCoop).GroupBy(x => x.UserId);

            foreach(var userXref in userXrefs) {
                var xrefs = userXref.OrderByDescending(x => x.CreatedOn).ToList();
                foreach(var xref in xrefs) {
                    //if(xref.RunningScore == null) {
                    var lastFourXrefs = xrefs.Where(x => x.CreatedOn <= xref.CreatedOn && x.Score.HasValue).Take(4).ToList();
                    if(lastFourXrefs.Count == 4 && xref.Score.HasValue) {
                        xref.RunningScore = lastFourXrefs.Average(x => x.Score);
                    } else {
                        xref.RunningScore = null;
                    }
                    //}
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
        }

        public async Task<IActionResult> Sleepers() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var demeritExpires = DateTimeOffset.Now.AddDays(-2);
            var sleepers = await _db.UserCoopXrefs.AsQueryable().Where(x => x.User.GuildId == guildId && !x.Coop.DeletedChannel).Select(x => new SleeperDetail {
                DiscordName = x.User.DiscordUsername,
                CurrentSleep = x.HoursSleeping - (x.SiloTimeHours ?? 0),
                TotalCoopSleep = x.TotalHoursSleeping,
                CoopName = x.Coop.Name,
                ContractName = x.Coop.Contract.Name,
                DiscordChannelId = x.Coop.DiscordChannelId,
                GuildId = guildId,
                Demerits = x.User.Demerits.Where(y => y.When > demeritExpires).ToList(),
                FreshEgg = x.User.Registered > DateTimeOffset.Now.AddDays(-7)
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
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var dbguild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            var guild = _discord.Guilds.First(x => x.Id == guildId);
            await guild.DownloadUsersAsync();
            var needToJoinChannel = guild.TextChannels.FirstOrDefault(x => x.Id == 775558629671698442);
            var allMessages = needToJoinChannel is null ? new List<IMessage>() : await needToJoinChannel.GetMessagesAsync(1000).FlattenAsync();
            var allMentions = allMessages.SelectMany(x => x.MentionedUserIds);



            var allGhosts = new List<Ghost>();
            foreach(var overflowGuildId in dbguild.OverflowServers) {
                var overflowGuild = _discord.Guilds.First(x => x.Id == overflowGuildId);
                await overflowGuild.DownloadUsersAsync();
                var oneWeekAgo = DateTimeOffset.Now.AddDays(-7);
                var xrefs = await _db.UserCoopXrefs.AsQueryable().Where(x => !x.Coop.DeletedChannel && x.Coop.OverflowGuildId == overflowGuildId && !x.JoinedCoop).Select(x => new Ghost {
                    Coop = x.Coop.Name,
                    DiscordId = x.User.DiscordId,
                    CoopChannel = x.Coop.DiscordChannelId,
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
                    if(discorduser is not null && !discorduser.Roles.Any(x => x.Id == 775547850134257675)) {
                        await message.DeleteAsync();
                    }
                }
            }
            return View(allGhosts);
        }

        public async Task<IActionResult> DeleteGhost([FromQuery] Guid UserId, [FromQuery] Guid CoopId) {
            var xref = await _db.UserCoopXrefs.AsQueryable().FirstAsync(x => x.UserId == UserId && x.CoopId == CoopId);
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
                    ExpireCustomCoopName =  x.ExpireCustomCoopName, 
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
                    IdentityUser = x,
                    IdentityUserRoles = userRoles.Where(y => y.UserId == x.Id).ToList(),
                    DiscordId = DiscordId,
                    CustomCoopName = customName?.CustomCoopName,
                    ExpireCustomCoopName = customName?.ExpireCustomCoopName
                };
            });



            return View(new EditUserModel {
                Users = editUserList.ToList(),
                Roles = roles
            });
        }

        public class EditUserModel {
            public List<EditUserWithDetails> Users { get; set; }
            public List<IdentityRole> Roles { get; set; }
        }

        public class EditUserWithDetails {
            public string CustomCoopName { get; set; }
            public DateTimeOffset? ExpireCustomCoopName { get; set; }
            public Guid DBUserId { get; set; }
            public string DiscordId { get; set; }
            public IdentityUser IdentityUser { get; set; }
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

        public async Task<IActionResult> SearchID([FromQuery] string id) {
            var users = await _db.DBUsers.AsQueryable().Select(x => new { x.Id, x.DiscordId, x.DiscordUsername, x._eggIncIds }).ToListAsync();

            if(id.StartsWith("EI")) {

                var matchingUser = users.FirstOrDefault(x => x._eggIncIds?.Contains(id) ?? false);
                if(matchingUser != null) {
                    return RedirectToAction("ViewUser", "MyFarms", new { discordId = matchingUser.DiscordId });
                }

                return RedirectToAction("ViewUserId", "MyFarms", new { eggIncId = id });
            } else if(id.Trim().All(x => x >= '0' && x <= '9')) {
                return RedirectToAction("ViewUser", "MyFarms", new { discordId = id });
            }

            var matchingUser2 = users.FirstOrDefault(x => x.DiscordUsername?.Contains(id) ?? false);
            if(matchingUser2 != null) {
                return RedirectToAction("ViewUser", "MyFarms", new { discordId = matchingUser2.DiscordId });
            }

            return RedirectToAction("Index");
        }

        public async Task<ActionResult> DuplicateChannels() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guildId);


            var coops = await _db.Coops.AsQueryable().Where(x => x.GuildId == guildId && x.DeletedChannel == false).ToListAsync();

            var coopChannels = _discord.Guilds.Where(x => x.Id == guildId || dbguild.OverflowServers.Any(y => y == x.Id)).SelectMany(x => x.TextChannels);

            var coopsWithChannels = coops.Select(c => new CoopWithChannels { Coop = c, MainChannel = coopChannels.FirstOrDefault(x => x.Id == c.DiscordChannelId), ExtraChannels = coopChannels.Where(x => x.Id != c.DiscordChannelId && StripEmoji(x.Name).Equals(c.Name, StringComparison.CurrentCultureIgnoreCase)).ToList() }).ToList();

            return View(coopsWithChannels.Where(x => x.ExtraChannels.Any() || x.MainChannel is null).ToList());
        }


        public async Task<ActionResult> Deleteduplicate([FromQuery] ulong id) {
            var channel = (ITextChannel)(_discord.Guilds.SelectMany(x => x.TextChannels.Where(x => x.Id == id)).First());
            await channel.DeleteAsync();
            return RedirectToAction("DuplicateChannels");
        }
        public async Task<ActionResult> DeleteAllDuplicates() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guildId);


            var coops = await _db.Coops.AsQueryable().Where(x => x.GuildId == guildId && x.DeletedChannel == false).ToListAsync();

            var coopChannels = _discord.Guilds.Where(x => x.Id == guildId || dbguild.OverflowServers.Any(y => y == x.Id)).SelectMany(x => x.TextChannels);

            var coopsWithChannels = coops.Select(c => new CoopWithChannels { Coop = c, MainChannel = coopChannels.FirstOrDefault(x => x.Id == c.DiscordChannelId), ExtraChannels = coopChannels.Where(x => x.Id != c.DiscordChannelId && StripEmoji(x.Name).Equals(c.Name, StringComparison.CurrentCultureIgnoreCase)).ToList() }).ToList();

            foreach(var channel in coopsWithChannels.Where(x => x.ExtraChannels.Any()).SelectMany(x => x.ExtraChannels)) {
                await ((ITextChannel)channel).DeleteAsync();
            }
            return RedirectToAction("DuplicateChannels");
        }

        public async Task<ActionResult> Modifiers() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            //var dbguild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            //var users = await _db.DBUsers.Where(x => x.GuildId == guildId).ToListAsync();
            var users = await _db.DBUsers.Where(x => x.GuildId != guildId).ToListAsync();
            var backupWithUsers = users.Where(x => x.Backups != null).SelectMany(x => x.Backups.Select(y => new BackupWithUser { User = x, Backup = y })).ToList();
            var tooManyPEBackups = backupWithUsers.Where(x => x.Backup.EggsOfProphecy > x.Backup.PEFromDailyGifts + x.Backup.PEFromTrophies + (x.Backup.ArchivedFarms?.Sum(x => x.PEGained) ?? 0)).ToList();

            var usersToUpdate = tooManyPEBackups.Where(x => x.Backup.PEFromTrophies == -1).Take(500).GroupBy(x => x.User.Id);
            foreach(var backup in usersToUpdate) {
                var user = backup.First().User;
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
            }
            await _db.SaveChangesAsync();


            return View(tooManyPEBackups);
        }

        public class BackupWithUser {
            public DBUser User { get; set; }
            public CustomBackup Backup { get; set; }
        }

        private string StripEmoji(string text) {
            return Regex.Replace(text, @"\p{Cs}", "");
        }

        public class CoopWithChannels {
            public Coop Coop { get; set; }
            public SocketTextChannel MainChannel { get; set; }
            public List<SocketTextChannel> ExtraChannels { get; set; }
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
                eggsFound = new Dictionary<EasterUser, int>();
                foreach(Match match in matches) {
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

                            var needsProPermit = dbuser.Backups.Any(x => dbuser.EggIncIds.Any(y => x.EggIncId == y.Id) && x.PermitLevel == 0);
                            eggsFound.Add(new EasterUser { User = guildUser, NeedsProPermit = needsProPermit }, 1);
                        }
                    }
                }

                _cache.Set(easterCacheKey, eggsFound, TimeSpan.FromMinutes(10));
            }
            return View(eggsFound);
        }

        public async Task<IActionResult> ConfigureServer() {
            var dbGuild = await _db.Guilds.FirstAsync(x => x.Id == GetGuildID());

            return View(dbGuild);
        }

        public async Task<IActionResult> SaveChannelDetails([FromForm]string json) {
            Console.WriteLine(json);
            var model = JsonConvert.DeserializeObject<SaveChannelDetailsObject>(json);
            var dbGuild = await _db.Guilds.FirstAsync(x => x.Id == GetGuildID());
            dbGuild.ChannelDetails = model.channelDetails;
            dbGuild.CoopCategories = model.coopCategories;
            dbGuild.FinishedCategories = model.finishedCategories;
            await _db.SaveChangesAsync();

            return Ok();
        }

        public ulong GetGuildID() {
            return ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
        }

        public class SaveChannelDetailsObject {
            public List<ChannelDetail> channelDetails { get; set; }
            public string coopCategories { get; set; }
            public string finishedCategories { get; set; }
        }

        public async Task<IActionResult> SaveCoopCategories(List<ulong> coopCategories) {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var dbGuild = await _db.Guilds.FirstAsync(x => x.Id == guildId);
            dbGuild.CoopCategories = string.Join(",", coopCategories);
            await _db.SaveChangesAsync();

            return Ok();
        }

        public async Task<IActionResult> FinishedCategories(List<ulong> finishedCategories) {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var dbGuild = await _db.Guilds.FirstAsync(x => x.Id == guildId);
            dbGuild.FinishedCategories = string.Join(",", finishedCategories);
            await _db.SaveChangesAsync();

            return Ok();
        }

        public class EasterUser {
            public RestGuildUser User { get; set; }
            public bool NeedsProPermit { get; set; }
        }

        public async Task<IActionResult> StandardPermit() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var users = await _db.DBUsers.Where(x => x.GuildId == guildId && !x.TempDisabled).ToListAsync();
            users = users.Where(x => x.Backups?.Any(y => y.PermitLevel == 0) ?? false).ToList();

            var userids = users.Select(x => x.Id).ToArray();
            ViewBag.Demerits = await _db.Demerit.Where(x => userids.Contains(x.UserId)).ToListAsync();
            ViewBag.Merits = await _db.Merit.Where(x => userids.Contains(x.UserId)).ToListAsync();
            ViewBag.Xrefs = await _db.UserCoopXrefs.Where(x => x.Score.HasValue && userids.Contains(x.UserId)).ToListAsync();

            return View(users);
        }
    }
}