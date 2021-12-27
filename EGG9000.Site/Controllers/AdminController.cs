using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Policy;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EGG9000.Site.Controllers {
    [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
    public class AdminController : Controller {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly DiscordSocketClient _discord;

        public AdminController(
            UserManager<IdentityUser> userManager,
            DiscordSocketClient discord,
            ApplicationDbContext db) {
            _db = db;
            _userManager = userManager;
            _discord = discord;
        }

        public async Task<IActionResult> Index() {
            var guildId = ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
            var guild = await _db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == guildId);

            var coops = await _db.Coops.AsQueryable().Select(x => new { x.Created, Finished = x.CoopCompleted ?? x.CoopEnds }).ToListAsync();

            var days = new Dictionary<DateTimeOffset, int>();
            coops = coops.Where(x => x.Created != DateTimeOffset.MinValue).ToList();
            for(var start = coops.OrderBy(x => x.Created).First().Created.Date; start <= DateTimeOffset.Now; start = start.AddDays(1)) {
                var count = coops.Count(c => c.Created.Date <= start && (c.Finished?.Date ?? c.Created.AddDays(4).Date) >= start);

                days.Add(start, count);
            }

            return View(new IndexViewModel {
                Contracts = await _db.Contracts.AsQueryable().OrderByDescending(x => x.Created).Take(10).ToListAsync(),
                Guilds = _discord.Guilds.Where(x => x.Id == guildId || guild.OverflowServers.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new GuildDetails {
                    Name = x.Name,
                    ChannelCount = x.Channels.Count,
                    ActiveCoops = x.TextChannels.Where(c => c.Category != null).Count(c => c.Category.Name.Contains("coops") && !c.Category.Name.Contains("finished")),
                    FinishedCoops = x.TextChannels.Where(c => c.Category != null).Count(c => c.Category.Name.Contains("coops") && c.Category.Name.Contains("finished")),
                }).ToList(),
                Days = days
            });
        }

        public class IndexViewModel {
            public List<Contract> Contracts { get; set; }
            public List<GuildDetails> Guilds { get; set; }
            public Dictionary<DateTimeOffset, int> Days { get; set; }
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
                x.CreateOn
            }).ToListAsync();
            var dbusers = rawusers.Select(x => new DBUser { DiscordId = x.DiscordId, DiscordUsername = x.DiscordUsername, GuildId = x.GuildId, Id = x.Id, _CustomBackups = x._CustomBackups, _eggIncIds = x._eggIncIds, CreateOn = x.CreateOn });

            //await _db.Users.AsQueryable().Where(x => (x.GuildId == user.GuildId || all) && x._LastBackup != null).ToListAsync()
            return View(dbusers.Where(x => x.Backups != null).ToList());
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
                FreshEgg = x.User.CreateOn > DateTimeOffset.Now.AddDays(-7)
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
            var needToJoinChannel = guild.TextChannels.First(x => x.Id == 775558629671698442);
            var allMessages = await needToJoinChannel.GetMessagesAsync(1000).FlattenAsync();
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
                    UserId = x.UserId
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
                    var discorduser = guild.Users.First(x => x.Id == message.MentionedUserIds.First());
                    if(!discorduser.Roles.Any(x => x.Id == 775547850134257675)) {
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
            var userRoles = await _db.UserRoles.AsQueryable().ToListAsync();
            var roles = await _db.Roles.AsQueryable().ToListAsync();
            return View(new EditUserModel {
                UserRoles = userRoles,
                Users = users,
                Roles = roles
            });
        }

        public class EditUserModel {
            public List<IdentityUser> Users { get; set; }
            public List<IdentityRole> Roles { get; set; }
            public List<IdentityUserRole<string>> UserRoles { get; set; }
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

        [Authorize(Roles ="Admin")]
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
    }
}