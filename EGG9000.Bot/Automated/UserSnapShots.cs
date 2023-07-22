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
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Automated {
    public class UserSnapShots : _UpdaterBase<UserSnapShots> {

        public UserSnapShots(
            IServiceProvider provider
        ) : base(TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var hasSnapshots = await _db.UserSnapShots.AsQueryable().AnyAsync(x => x.Date == DateTime.Now.Date);

            if(!hasSnapshots) {
                var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId != 0).ToListAsync();
                var snapshots = 0;
                foreach(var user in users) {
                    try {
                        foreach(var account in user.EggIncAccounts) {
                            var backup = account.Backup;
                            var lastSnapshot = await _db.UserSnapShots.AsQueryable().FirstOrDefaultAsync(x => 
                                x.UserId == user.Id &&
                                x.Date == DateTime.Now.Date && 
                                x.EggIncID == account.Id
                            );
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
                                _logger.LogTrace("Adding Snapshot for {0}", user.Id);
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
