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
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class UserSnapShots : _UpdaterBase<UserSnapShots> {

        public UserSnapShots(
            IServiceProvider provider
        ) : base(TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);

            var hasSnapshots = await _db.UserSnapShots.AsQueryable().AnyAsync(x => x.Date == DateTime.Now.Date);

            if(!hasSnapshots) {
                var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId != 0 && x._CustomBackups != null).ToListAsync();
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
