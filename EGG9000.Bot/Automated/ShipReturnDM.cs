using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Common.Helpers;
using Ei;
using Humanizer;
using Discord.Net;
using EGG9000.Common.Services;
using static EGG9000.Common.Database.Entities.DBUser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RazorEngine.Compilation.ImpromptuInterface.Dynamic;
using EGG9000.Bot.Common.Helpers;

namespace EGG9000.Bot.Automated {
    public class ShipReturnDM : _UpdaterBase<ShipReturnDM> {
        public ShipReturnDM(
            IServiceProvider provider
        ) : base(TimeSpan.FromSeconds(15), TimeSpan.Zero, provider) {
        }
        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId > 0 && x.DMOnShipReturn && x.NextShipReturnDMDue <= DateTimeOffset.Now).ToListAsync();
            foreach(var user in users) {
                var discordUser = _client.GetUser(user.DiscordId);
                if(discordUser == null) {
                    continue;
                }
                var dmChannel = await discordUser.CreateDMChannelAsync();


                foreach(var shipDm in user.ShipDMs.Where(x => x.DMTime <= DateTimeOffset.Now.AddSeconds(30) && !x.Sent)) {
                    try {
                        var message = "";
                        var backup = user.EggIncAccounts.First(x => shipDm.EggIncID == null || x.Id == shipDm.EggIncID).Backup;
                        var mission = backup.SpaceMissions.FirstOrDefault(m => m.ReturnTime == shipDm.ShipReturnTime);
                        if(mission == null) {
                            shipDm.Sent = true;
                            continue;
                        }

                        var fuelingShip = backup.SpaceMissions.FirstOrDefault(m => m.Status == MissionInfo.Types.Status.Fueling);
                        var needsFuel = NeedsFuel(backup);
                        var nextShipDue = DateTimeOffset.FromUnixTimeSeconds(mission.ReturnTime);//.AddMinutes(needsFuel ? user.ShipReturnStillFuelingMinutes : user.ShipReturnMinutes);
                        var nextShipName = mission.Ship.ToString().Replace("_", " ");
                        var minutesUntilShipReturns = (DateTimeOffset.Now - nextShipDue).Humanize(2);

                        var lastBackupTime = (DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime)).Humanize(2).ShortenTime();

                        if(needsFuel && nextShipDue > DateTimeOffset.Now) {
                            message = $"Your {nextShipName} returns in {minutesUntilShipReturns} and your current ship needs fueling. Last backup {lastBackupTime}.";
                        } else if(needsFuel && nextShipDue <= DateTimeOffset.Now) {
                            message = $"Your {nextShipName} has returned and your current ship needs fueling. Last backup {lastBackupTime}.";
                        } else if(!needsFuel && nextShipDue > DateTimeOffset.Now) {
                            message = $"Your {nextShipName} returns in {minutesUntilShipReturns} and your current ship is ready.";
                        } else {
                            message = $"Your {nextShipName} has returned and your current ship is ready.";
                        }

                        if(user.EggIncAccounts.Count > 1) {
                            message += $" (For {backup.UserName})";
                        }

                        try {
                            if(backup.FuelAmounts is not null && backup.FuelAmounts.Count > 0) {
                                double tankSize = 0;
                                switch(backup.TankLevel) {
                                    case 0: tankSize = 2_000_000_000; break;
                                    case 1: tankSize = 200_000_000_000; break;
                                    case 2: tankSize = 10_000_000_000_000; break;
                                    case 3: tankSize = 100_000_000_000_000; break;
                                    case 4: tankSize = 200_000_000_000_000; break;
                                    case 5: tankSize = 300_000_000_000_000; break;
                                    case 6: tankSize = 400_000_000_000_000; break;
                                    case 7: tankSize = 500_000_000_000_000; break;
                                }

                                message += $"\n{String.Join("\n", backup.FuelAmounts.Select(x => $"{EggIncEggs.GetEggById(x.Key).Emoji} - {x.Value.ToEggString()} ({Math.Round(x.Value / tankSize * 100)}%)"))}";
                            }
                        } catch(Exception e) {
                            _bugsnag.Notify(e);
                        }

                        shipDm.Sent = true;
                        user.ShipDMs = user.ShipDMs;
                        

                        var shipReturnTime = DateTimeOffset.FromUnixTimeSeconds(shipDm.ShipReturnTime);
                        if(shipReturnTime > DateTimeOffset.Now.AddMinutes(-5)) {
                            if(shipReturnTime > DateTimeOffset.Now) {
                                _logger.LogInformation("Sending on time ShipReturnDM to {user}", user.DiscordUsername);
                            } else {
                                _logger.LogInformation("Sending late ShipReturnDM to {user}, the ship returned {relativetime} ago", user.DiscordUsername, (DateTimeOffset.Now - shipReturnTime).Humanize().ShortenTime());
                            }
                            await Task.Delay(500);
                        } else {
                            _logger.LogWarning("Too late to send ShipReturnDM to {user}, the ship returned {relativetime} ago", user.DiscordUsername, (DateTimeOffset.Now - shipReturnTime).Humanize().ShortenTime());
                        }

                        var retEx = await DiscordHelpersExt.BoolSendDm(dmChannel, message);
                        var dbUser = _db.DBUsers.FirstOrDefault(u => u.DiscordId == discordUser.Id);
                        if(dbUser is not null && (retEx == null) == dbUser.DMSBlocked) {
                            dbUser.DMSBlocked = !dbUser.DMSBlocked;
                        }
                        if(retEx != null) {
                            _logger.LogError(retEx, "User {user} has DMs blocked", discordUser.Username);
                            var dbguild = await _db.Guilds.FirstAsync(x => x.DiscordSeverId == user.GuildId);
                            var socketGuild = _client.Guilds.FirstOrDefault(g => g.Id ==  dbguild.Id);
                            var response = await ChannelHelper.DetermineAndSend(dbguild, socketGuild, GuildChannelType.WarningMessagesForUser, new() 
                            { Text = $"<@{user.DiscordId}> you have elected to receive DMs for Ship Return status, but have blocked the bot from sending you DMs" });
                        }
                        await _db.SaveChangesAsync();
                    } catch(Exception e) {
                        _bugsnag.Notify(e);
                        _logger.LogError(e, "UpdateNextShipDM Error");
                    }
                }
            }
            var timespan = await UpdateNextShipDM(users, _db, _logger, justSent: true);
            this.ChangeUpdateInterval(timespan);
            await _db.SaveChangesAsync();
        }

        public static async Task<TimeSpan> UpdateNextShipDM(List<DBUser> dbusers, ApplicationDbContext _db, ILogger logger, bool justSent = false) {
            foreach(var user in dbusers.Where(x => x.DMOnShipReturn)) {
                try {

                    List<ShipDM> currentShipDMs = new List<ShipDM>();

                    foreach(var b in user.EggIncAccounts.Where(x => x.Backup?.SpaceMissions != null)) {
                        var needsFuel = NeedsFuel(b.Backup);

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


                    user.ShipDMs = currentShipDMs.Where(x => x.DMTime > DateTimeOffset.Now).ToList();
                    var NextShipReturnDMDue = currentShipDMs.Where(x => !x.Sent).OrderBy(x => x.DMTime).FirstOrDefault()?.DMTime;

                    if(NextShipReturnDMDue != user.NextShipReturnDMDue) {
                        var dbuser = await _db.DBUsers.AsQueryable().FirstAsync(x => x.Id == user.Id);
                        dbuser.NextShipReturnDMDue = NextShipReturnDMDue;
                        logger.LogInformation("Updating next ship time for {user} to {time}", user.DiscordUsername, NextShipReturnDMDue?.ToString("h:mm") ?? "null");
                    }
                } catch(Exception e) {
                    logger.LogError(e, $"UpdateNextShipDM Error");
                }
            }
            await _db.SaveChangesAsync();

            var earliestNextTime = dbusers.Where(x => x.DMOnShipReturn && x.NextShipReturnDMDue is not null && x.NextShipReturnDMDue > DateTimeOffset.Now).OrderBy(x => x.NextShipReturnDMDue).FirstOrDefault();
            if(earliestNextTime is not null) {
                var timeToNext = (earliestNextTime.NextShipReturnDMDue.Value - DateTimeOffset.Now);
                logger.LogInformation("Next return time {time}", timeToNext);
                if(timeToNext > TimeSpan.Zero && timeToNext < TimeSpan.FromMinutes(5)) {
                    return timeToNext;
                }
            }
            return TimeSpan.FromMinutes(1);
        }

        private static bool NeedsFuel(CustomBackup backup) {
            bool needsFuel = true;
            var currentShip = backup.SpaceMissions.FirstOrDefault(x => x.Status == MissionInfo.Types.Status.Fueling);
            if(currentShip != null) {
                var fuelTargets = Ei.MissionInfo.GetFuelTargets(currentShip.Ship, currentShip.Duration);
                needsFuel = fuelTargets.Any(ft => ft.Value > (currentShip.Fuels.FirstOrDefault(f => f.Egg == ft.Key)?.Amount ?? 0));
            }
            return needsFuel;
        }
    }
}
