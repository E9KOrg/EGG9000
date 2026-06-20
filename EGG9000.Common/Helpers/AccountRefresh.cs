using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    // Shared per-account refresh primitives. RefreshBackupAsync pulls and assigns a fresh backup;
    // ApplyExtrasAsync pulls the "alt" data (grade + per-season CXP) that is too expensive to run on
    // every mass backup. Both mutate the account in memory and stage DB changes; the caller persists
    // (UpdateAccounts + SaveChanges, or a targeted ExecuteUpdate). Compose them as needed.
    public static class AccountRefresh {

        // Pulls a fresh backup for one account and assigns it (carry-forward via the prior backup so
        // monotonic data like colleggtible levels is preserved). Returns the new backup, or null if the
        // API failed or returned no farms. In-memory only - caller persists the account blob.
        public static async Task<CustomBackup> RefreshBackupAsync(EggIncAccount account, FrozenSet<Ei.Contract> cachedContracts) {
            var firstContact = await EggIncApi.FirstContact(account.Id);
            if(firstContact?.Backup is null)
                return null;

            var backup = new CustomBackup(firstContact.Backup, cachedContracts, account.Backup);
            if(backup?.Farms is null)
                return null;

            account.Backup = backup;
            return backup;
        }

        // Pulls grade + per-season CXP from get_contract_player_info and applies them: grade via GradeSync
        // (in memory, repacks the account blob) and UserSeasonProgress upserts staged on `db` (NOT saved).
        // Caller persists the account blob and calls SaveChanges. Returns whether the grade changed.
        public static async Task<bool> ApplyExtrasAsync(DBUser user, EggIncAccount account, ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken = default) {
            var (info, error) = await EggIncApi.GetContractPlayerInfo(account.Id);
            if(info is null) {
                logger.LogWarning("No response getting grade for user {User} ({Account}): {Error}", user.DiscordUsername, account.Name, error);
                return false;
            }
            if(info.Status != Ei.ContractPlayerInfo.Types.Status.Complete) {
                logger.LogTrace("Skipping non-final grade ({Status}) for user {User} ({Account})", info.Status, user.DiscordUsername, account.Name);
                return false;
            }

            var gradeChanged = GradeSync.ApplyGradeChange(user, account, info.Grade, setPromotionTime: false, guardUnset: true, logger);
            if(!gradeChanged)
                logger.LogInformation("No grade change for user {User} ({Account}) grade: {Grade}", user.DiscordUsername, account.Name, info.Grade);

            await UpsertSeasonProgress(account.Id, info.SeasonProgress, db, cancellationToken);
            return gradeChanged;
        }

        private static async Task UpsertSeasonProgress(string eggIncId, IEnumerable<Ei.ContractPlayerInfo.Types.SeasonProgress> seasonProgress, ApplicationDbContext db, CancellationToken cancellationToken) {
            var rows = seasonProgress.Where(sp => !string.IsNullOrEmpty(sp.SeasonId)).ToList();
            if(rows.Count == 0)
                return;

            var seasonIds = rows.Select(sp => sp.SeasonId).ToList();
            var existing = await db.UserSeasonProgresses
                .Where(x => x.EggIncId == eggIncId && seasonIds.Contains(x.SeasonId))
                .ToListAsync(cancellationToken);

            foreach(var sp in rows) {
                var row = existing.FirstOrDefault(x => x.SeasonId == sp.SeasonId);
                if(row is null) {
                    db.UserSeasonProgresses.Add(new UserSeasonProgress {
                        EggIncId = eggIncId,
                        SeasonId = sp.SeasonId,
                        TotalCxp = sp.TotalCxp,
                        StartingGrade = (int)sp.StartingGrade
                    });
                } else {
                    row.TotalCxp = sp.TotalCxp;
                    row.StartingGrade = (int)sp.StartingGrade;
                }
            }
        }
    }
}
