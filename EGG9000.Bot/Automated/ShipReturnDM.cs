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
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class ShipReturnDM : _UpdaterBase<ShipReturnDM> {
        public ShipReturnDM(
            IServiceProvider provider
        ) : base(TimeSpan.FromSeconds(15), TimeSpan.Zero, provider) {
        }
        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);

            var users = await _db.DBUsers.AsQueryable().Where(x => x.DMOnShipReturn && x.NextShipReturnDMDue <= DateTimeOffset.Now).ToListAsync();
            foreach(var user in users) {
                var discordUser = _client.GetUser(user.DiscordId);
                if(discordUser == null) {
                    continue;
                }
                var dmChannel = await discordUser.CreateDMChannelAsync();

                
                foreach(var shipDm in user.ShipDMs.Where(x => x.DMTime <= DateTimeOffset.Now && !x.Sent)) {
                    try {
                        var message = "";
                        var backup = user.Backups.First(x => shipDm.EggIncID == null || x.EggIncId == shipDm.EggIncID);
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

                        if(user.Backups.Count > 1) {
                            message += $" (For {backup.UserName})";
                        }
                        shipDm.Sent = true;
                        try {
                            await dmChannel.SendMessageAsync(message);
                        } catch (HttpException) {
                            var dbguild = await _db.Guilds.FirstAsync(x => x.DiscordSeverId == user.GuildId);
                            if(dbguild.ChannelDetails.Any(y => y.ChannelType == GuildChannelType.WarningMessagesForUser)) {
                                var talkChannel = _client.GetGuild(user.GuildId).GetTextChannel(dbguild.ChannelDetails.First(y => y.ChannelType == GuildChannelType.WarningMessagesForUser).Id);

                                await talkChannel.SendMessageAsync($"<@{user.DiscordId}> you have elected to receive DMs for Ship Return status, but have blocked the bot from sending you DMs");
                            }
                        }
                        await _db.SaveChangesAsync();
                    } catch (Exception e) {

                        _bugsnag.Notify(e);

                        Console.WriteLine($"UpdateNextShipDM Error: {e.Message}");
                    }
                }

                user.ShipDMs = user.ShipDMs.ToList();
            }
            await UpdateNextShipDM(users, _db, justSent: true);
            await _db.SaveChangesAsync();
        }

        public static async Task UpdateNextShipDM(List<DBUser> dbusers, ApplicationDbContext _db, bool justSent = false) {
            foreach(var user in dbusers.Where(x => x.DMOnShipReturn)) {
                try {
                    var currentShipDMs = user.Backups.Where(x => x.SpaceMissions != null).SelectMany(b => {
                        var needsFuel = NeedsFuel(b);

                        return b.SpaceMissions.Where(m => m.Status != MissionInfo.Types.Status.Fueling).Select(m => new ShipDM {
                            EggIncID = user.EggIncIds.Count > 1 ? b.EggIncId : null,
                            ShipReturnTime = m.ReturnTime,
                            DMTime = DateTimeOffset.FromUnixTimeSeconds(m.ReturnTime).AddMinutes(0 - (needsFuel ? user.ShipReturnStillFuelingMinutes : user.ShipReturnMinutes)),
                            Sent = user.ShipDMs?.FirstOrDefault(d => d.ShipReturnTime == m.ReturnTime && d.EggIncID ==( user.EggIncIds.Count > 1 ? b.EggIncId : null))?.Sent ?? false
                        });
                    });


                    user.ShipDMs = currentShipDMs.ToList();
                    var NextShipReturnDMDue = currentShipDMs.Where(x => !x.Sent).OrderBy(x => x.DMTime).FirstOrDefault()?.DMTime;

                    if(NextShipReturnDMDue != user.NextShipReturnDMDue) {
                        var dbuser = await _db.DBUsers.AsQueryable().FirstAsync(x => x.Id == user.Id);
                        dbuser.NextShipReturnDMDue = NextShipReturnDMDue;
                        Console.WriteLine($"Updating next ship time for {user.DiscordUsername} to {NextShipReturnDMDue?.ToString("h:mm") ?? "null"}");
                    }
                } catch(Exception e) {
                    Console.WriteLine($"UpdateNextShipDM Error: {e.Message}");
                }
            }
            await _db.SaveChangesAsync();
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
