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
    public class UserSnapShots(IServiceProvider provider) : _UpdaterBase<UserSnapShots>(TimeSpan.FromHours(5), TimeSpan.FromMinutes(1), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var eggDayDate = new DateTimeOffset(DateTimeOffset.UtcNow.Year, 07, 14, 10, 55, 0, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time").GetUtcOffset(DateTimeOffset.UtcNow));

            var eggDay1 = DateTimeOffset.UtcNow.Date == eggDayDate.Date;
            var eggDay = DateTimeOffset.UtcNow > eggDayDate && DateTimeOffset.UtcNow < eggDayDate.AddDays(1).AddMinutes(30);
            var eggDay2 = DateTimeOffset.UtcNow.Date == eggDayDate.AddDays(1).Date;

            if(eggDay1 && !eggDay)
                return;
            if(eggDay2 && eggDay)
                return;

            // Egg-day gains compare the last snapshot before the event against the first
            // one after, so those boundary rows are written even when nothing changed.
            var forceWrite = eggDay1 || eggDay2;

            var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId != 0).ToListAsync(CancellationToken.None);
            var snapshots = 0;
            foreach(var user in users) {
                await WaitOnCoopsBeingCreated(cancellationToken);

                try {
                    foreach(var account in user.EggIncAccounts) {
                        var backup = account.Backup;
                        var lastSnapshot = await _db.UserSnapShots.AsQueryable()
                            .Where(x => x.UserId == user.Id && x.EggIncID == account.Id)
                            .OrderByDescending(x => x.Date)
                            .FirstOrDefaultAsync(cancellationToken);
                        // Per-account idempotency: one row per account per day. This is the only
                        // per-day guard, so a partial run (cancel/restart) is safe to re-run -
                        // accounts already snapped today are skipped and missed ones get picked
                        // up on the next pass, instead of a single global flag starving the tail.
                        if(lastSnapshot?.Date == DateTime.UtcNow.Date) continue;

                        // Skip when nothing changed since the last snapshot; a date gap
                        // in the table means "unchanged", not "missing".
                        var unchanged = lastSnapshot is not null
                            && lastSnapshot.Prestiges == backup.NumPrestiges
                            && lastSnapshot.EarningsBonus == backup.EarningsBonus
                            && lastSnapshot.EggsOfProphecy == backup.EggsOfProphecy
                            && lastSnapshot.SoulEggs == backup.SoulEggs
                            && lastSnapshot.EggsOfTruth == backup.EggsOfTruth
                            && lastSnapshot.CurrentEgg == backup.MaxEggReached
                            && lastSnapshot.CuriosityDelivered == backup.VirtueEggsDelivered.ElementAtOrDefault(0)
                            && lastSnapshot.IntegrityDelivered == backup.VirtueEggsDelivered.ElementAtOrDefault(1)
                            && lastSnapshot.HumilityDelivered == backup.VirtueEggsDelivered.ElementAtOrDefault(2)
                            && lastSnapshot.ResilienceDelivered == backup.VirtueEggsDelivered.ElementAtOrDefault(3)
                            && lastSnapshot.KindnessDelivered == backup.VirtueEggsDelivered.ElementAtOrDefault(4)
                            && lastSnapshot.TeTotal == backup.EggsOfTruthTotal
                            && lastSnapshot.TeEarned == backup.EggsOfTruth
                            && lastSnapshot.TePending == backup.EggsOfTruthTotal - (int)backup.EggsOfTruth
                            && lastSnapshot.ShiftCount == backup.ShiftCount
                            && lastSnapshot.Resets == backup.Resets;
                        if(unchanged && !forceWrite) continue;

                        _db.UserSnapShots.Add(new UserSnapShot {
                            Date = DateTime.UtcNow.Date,
                            UserId = user.Id,
                            Prestiges = backup.NumPrestiges,
                            EarningsBonus = backup.EarningsBonus,
                            EggIncID = backup.EggIncId,
                            EggsOfProphecy = backup.EggsOfProphecy,
                            SoulEggs = backup.SoulEggs,
                            EggsOfTruth = backup.EggsOfTruth,
                            CurrentEgg = backup.MaxEggReached,
                            CuriosityDelivered = backup.VirtueEggsDelivered.ElementAtOrDefault(0),
                            IntegrityDelivered = backup.VirtueEggsDelivered.ElementAtOrDefault(1),
                            HumilityDelivered = backup.VirtueEggsDelivered.ElementAtOrDefault(2),
                            ResilienceDelivered = backup.VirtueEggsDelivered.ElementAtOrDefault(3),
                            KindnessDelivered = backup.VirtueEggsDelivered.ElementAtOrDefault(4),
                            TeTotal = backup.EggsOfTruthTotal,
                            TeEarned = backup.EggsOfTruth,
                            TePending = backup.EggsOfTruthTotal - (int)backup.EggsOfTruth,
                            ShiftCount = backup.ShiftCount,
                            Resets = backup.Resets,
                        });
                        _logger.LogTrace("Adding Snapshot for {user}", user.Id);
                        if(snapshots++ >= 50) {
                            snapshots = 0;
                            await _db.SaveChangesAsync(cancellationToken);
                        }
                    }
                } catch(Exception e) {
                    _bugSnag.Notify(e);
                }
            }
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
