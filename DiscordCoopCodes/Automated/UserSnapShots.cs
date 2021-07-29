using Discord.WebSocket;
using DiscordCoopCodes.Database;
using DiscordCoopCodes.Database.Entities;
using DiscordCoopCodes.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscordCoopCodes.Helpers;
using Discord;
using EGG9000.Common.Helpers;
using Ei;
using Humanizer;
using EGG9000.Common.Database;

namespace DiscordCoopCodes.Automated {
    public class UserSnapShots : _UpdaterBase {
        private IConfiguration Configuration;

        public UserSnapShots(IConfiguration Configuration, DiscordSocketClient client,
            Bugsnag.IClient bugsnag) : base(TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), client, bugsnag) {
            this.Configuration = Configuration;
        }
        public override async Task Run(object state) {
            var _db = new ApplicationDbContext(Configuration["ConnectionStrings:DefaultConnection"]);

            var hasSnapshots = await _db.UserSnapShots.AsQueryable().AnyAsync(x => x.Date == DateTime.Now.Date);

            if(!hasSnapshots) {
                var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId != 0).ToListAsync();
                var snapshots = 0;
                foreach(var user in users) {
                    try {
                        foreach(var backup in user.Backups) {
                            var lastSnapshot = await _db.UserSnapShots.AsQueryable().FirstOrDefaultAsync(x => x.UserId == user.Id &&
                            x.Prestiges == backup.NumPrestiges &&
                            x.EarningsBonus == backup.EarningsBonus &&
                            x.EggIncID == backup.EggIncId &&
                            x.EggsOfProphecy == backup.EggsOfProphecy &&
                            x.SoulEggs == backup.SoulEggs);
                            if(lastSnapshot == null) {
                                _db.UserSnapShots.Add(new UserSnapShot {
                                    Date = DateTime.Now.Date,
                                    UserId = user.Id,
                                    Prestiges = backup.NumPrestiges,
                                    EarningsBonus = backup.EarningsBonus,
                                    EggIncID = backup.EggIncId,
                                    EggsOfProphecy = backup.EggsOfProphecy,
                                    SoulEggs = backup.SoulEggs

                                });
                                Console.WriteLine($"Added snapshot {user.DiscordUsername}");
                                if(snapshots++ >= 50) {
                                    snapshots = 0;
                                    await _db.SaveChangesAsync();
                                }
                            }
                        }
                    } catch(Exception e) {
                        _bugsnag.Notify(e);
                    }
                }
                await _db.SaveChangesAsync();
            }
        }
    }
}
