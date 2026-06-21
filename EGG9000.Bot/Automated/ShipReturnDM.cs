using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Ei;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Bot.Helpers.DiscordHelpersExt;
using static EGG9000.Common.Database.Entities.DBUser;

namespace EGG9000.Bot.Automated {
    public class ShipReturnDM(IServiceProvider provider) : _UpdaterBase<ShipReturnDM>(TimeSpan.FromSeconds(15), TimeSpan.Zero, provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbEggs = await _db.GetCustomEggsAsync();
            var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId > 0 && x.DMOnShipReturn && x.NextShipReturnDMDue <= DateTimeOffset.UtcNow).ToListAsync(CancellationToken.None);
            foreach(var user in users) {
                var discordUser = _client.GetUser(user.DiscordId);
                if(discordUser == null) {
                    continue;
                }

                foreach(var shipDm in user.ShipDMs.Where(x => x.DMTime <= DateTimeOffset.UtcNow.AddSeconds(30) && !x.Sent)) {
                    try {
                        var account = user.EggIncAccounts.First(x => shipDm.EggIncID == null || x.Id == shipDm.EggIncID);
                        var backup = account.Backup;
                        var mission = backup.SpaceMissions.FirstOrDefault(m => m.ReturnTime == shipDm.ShipReturnTime);
                        if(mission == null) {
                            shipDm.Sent = true;
                            user.ShipDMs = user.ShipDMs;
                            await _db.SaveChangesAsync(CancellationToken.None);
                            continue;
                        }

                        var shipReturnTime = DateTimeOffset.FromUnixTimeSeconds(shipDm.ShipReturnTime);
                        if(shipReturnTime <= DateTimeOffset.UtcNow.AddMinutes(-5)) {
                            _logger.LogWarning("Skipping stale ShipReturnDM for {user}, ship returned {relativetime} ago", user.DiscordUsername, (DateTimeOffset.UtcNow - shipReturnTime).Humanize().ShortenTime());
                            shipDm.Sent = true;
                            user.ShipDMs = user.ShipDMs;
                            await _db.SaveChangesAsync(CancellationToken.None);
                            continue;
                        }

                        var needsFuel = backup.NeedsFuel();
                        var nextShipDue = DateTimeOffset.FromUnixTimeSeconds(mission.ReturnTime);
                        var hasReturned = nextShipDue <= DateTimeOffset.UtcNow;

                        var fuelTank = new List<ShipReturnDmBuilder.FuelLine>();
                        if(backup.FuelAmounts is not null && backup.FuelAmounts.Count > 0) {
                            var tankSize = MissionHelpers.GetTankCapacity(backup.TankLevel);
                            foreach(var fa in backup.FuelAmounts) {
                                var pct = tankSize > 0 ? fa.Value / tankSize * 100 : 0;
                                // GetEggById returns null for a fuel egg the statics don't know about; fall
                                // back to the generic edible-egg emoji rather than dereferencing null.
                                var fuelEmoji = EggIncStatics.GetEggById(fa.Key, null, dbEggs)?.emoji ?? "<:Edible_Egg:712424206276755516>";
                                fuelTank.Add(new ShipReturnDmBuilder.FuelLine(fuelEmoji, fa.Value, pct));
                            }
                        }

                        var accountIndex = user.EggIncAccounts.FindIndex(a => a.Id == account.Id);
                        if(accountIndex < 0) accountIndex = 0;
                        var siteBaseUrl = _configuration["Site:BaseUrl"] ?? "https://egg9000.com";

                        var model = new ShipReturnDmBuilder.ShipReturnDmModel(
                            Ship: mission.Ship,
                            ReturnUnix: mission.ReturnTime,
                            LastBackupUnix: backup.LastBackupTime,
                            NeedsFuel: needsFuel,
                            HasReturned: hasReturned,
                            AccountName: backup.UserName,
                            MultiAccount: user.EggIncAccounts.Count > 1,
                            FuelTank: fuelTank,
                            UserId: user.DiscordId,
                            AccountIndex: accountIndex,
                            SiteBaseUrl: siteBaseUrl);

                        var (embed, components) = ShipReturnDmBuilder.Build(model);

                        if(shipReturnTime > DateTimeOffset.UtcNow) {
                            _logger.LogInformation("Sending on time ShipReturnDM to {user}", user.DiscordUsername);
                        } else {
                            _logger.LogInformation("Sending ShipReturnDM to {user}, the ship returned {relativetime} ago", user.DiscordUsername, (DateTimeOffset.UtcNow - shipReturnTime).Humanize().ShortenTime());
                        }

                        var capturedUser = discordUser;
                        var capturedEmbed = embed;
                        var capturedComponents = components;
                        var capturedDb = _db;
                        var dmResult = await _queue.EnqueueLowAsync(() => BoolSendDm(capturedUser, capturedEmbed, capturedComponents, capturedDb));

                        if(ShipReturnDmBuilder.ShouldMarkSent(dmResult)) {
                            shipDm.Sent = true;
                            user.ShipDMs = user.ShipDMs;
                        } else {
                            _logger.LogWarning("ShipReturnDM to {user} failed transiently ({result}); will retry next tick", user.DiscordUsername, dmResult);
                        }

                        await _db.SaveChangesAsync(CancellationToken.None);
                    } catch(Exception e) {
                        _bugSnag.Notify(e);
                        _logger.LogError(e, "UpdateNextShipDM Error");
                    }
                }
            }
            var timespan = await UpdateNextShipDM(users, _db);
            ChangeUpdateInterval(timespan);
            await _db.SaveChangesAsync(CancellationToken.None);
        }

        public static async Task<TimeSpan> UpdateNextShipDM(List<DBUser> dbusers, ApplicationDbContext _db) {
            foreach(var user in dbusers.Where(x => x.DMOnShipReturn)) {
                try {

                    List<ShipDM> currentShipDMs = [];

                    foreach(var b in user.EggIncAccounts.Where(x => x.Backup?.SpaceMissions != null)) {
                        var needsFuel = b.Backup.NeedsFuel();

                        foreach(var m in b.Backup.SpaceMissions.Where(m => m.Status != MissionInfo.Types.Status.Fueling)) {
                            currentShipDMs.Add(new ShipDM {
                                EggIncID = b.Id,
                                ShipReturnTime = m.ReturnTime,
                                DMTime = DateTimeOffset.FromUnixTimeSeconds(m.ReturnTime).AddMinutes(0 - (needsFuel && user.ShipReturnStillFuelingMinutes > 0 ? user.ShipReturnStillFuelingMinutes : user.ShipReturnMinutes)),
                                Sent = user.ShipDMs?.FirstOrDefault(d => d.ShipReturnTime == m.ReturnTime)?.Sent ?? false
                            });
                            if(user.ShipReturnDMAfterFuel) {
                                var returnTime = m.ReturnTime + 1;
                                currentShipDMs.Add(new ShipDM {
                                    EggIncID = b.Id,
                                    ShipReturnTime = returnTime,
                                    DMTime = DateTimeOffset.FromUnixTimeSeconds(m.ReturnTime).AddMinutes(0 - user.ShipReturnMinutes),
                                    Sent = user.ShipDMs?.FirstOrDefault(d => d.ShipReturnTime == returnTime)?.Sent ?? false
                                });
                            }
                        }
                    }


                    // Keep future DMs, plus any DM (sent or not) whose ship returned within the last
                    // 5 minutes. Retaining the sent record is what prevents duplicates: the mission
                    // stays in the backup until the ship is collected, so dropping a sent entry while
                    // its return is still live lets the next rebuild regenerate it as unsent and fire a
                    // second DM (the "returns soon" then "has returned" double-send). Once the return is
                    // >5 min old it is dropped here and Run() stale-skips any regenerated copy.
                    var staleReturnUnix = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
                    user.ShipDMs = currentShipDMs.Where(x => x.DMTime > DateTimeOffset.UtcNow || x.ShipReturnTime > staleReturnUnix).ToList();
                    var NextShipReturnDMDue = currentShipDMs.Where(x => !x.Sent && x.ShipReturnTime > staleReturnUnix).OrderBy(x => x.DMTime).FirstOrDefault()?.DMTime;

                    if(NextShipReturnDMDue != user.NextShipReturnDMDue) {
                        var dbuser = await _db.DBUsers.AsQueryable().FirstAsync(x => x.Id == user.Id);
                        dbuser.NextShipReturnDMDue = NextShipReturnDMDue;
                    }
                } catch(Exception) {}
            }
            await _db.SaveChangesAsync();

            var earliestNextTime = dbusers.Where(x => x.DMOnShipReturn && x.NextShipReturnDMDue is not null && x.NextShipReturnDMDue > DateTimeOffset.UtcNow).OrderBy(x => x.NextShipReturnDMDue).FirstOrDefault();
            if(earliestNextTime is not null) {
                var timeToNext = (earliestNextTime.NextShipReturnDMDue.Value - DateTimeOffset.UtcNow);
                if(timeToNext > TimeSpan.Zero && timeToNext < TimeSpan.FromMinutes(5)) {
                    return timeToNext;
                }
            }
            return TimeSpan.FromMinutes(1);
        }

    }
}
