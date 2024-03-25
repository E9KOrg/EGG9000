using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class UserSnapShots(IServiceProvider provider) : _UpdaterBase<UserSnapShots>(TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var hasSnapshots = await _db.UserSnapShots.AsQueryable().AnyAsync(x => x.Date == DateTime.Now.Date, cancellationToken);

            if(!hasSnapshots) {
                var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId != 0).ToListAsync(cancellationToken);
                var snapshots = 0;
                foreach(var user in users) {
                    try {
                        foreach(var account in user.EggIncAccounts) {
                            var backup = account.Backup;
                            var lastSnapshot = await _db.UserSnapShots.AsQueryable().FirstOrDefaultAsync(x => 
                                x.UserId == user.Id &&
                                x.Date == DateTime.Now.Date && 
                                x.EggIncID == account.Id,
                                cancellationToken
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
                                _logger.LogTrace("Adding Snapshot for {user}", user.Id);
                                if(snapshots++ >= 50) {
                                    snapshots = 0;
                                    await _db.SaveChangesAsync(cancellationToken);
                                }
                            }
                        }
                    } catch(Exception e) {
                        _bugsnag.Notify(e);
                    }
                }
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
