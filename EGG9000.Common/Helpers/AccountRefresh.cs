using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
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
        public static async Task<CustomBackup> RefreshBackupAsync(EggIncAccount account, FrozenSet<Ei.Contract> cachedContracts, ILogger logger = null) {
            var firstContact = await EggIncApi.FirstContact(account.Id, logger);
            if(firstContact?.Backup is null) {
                logger?.LogWarning("RefreshBackupAsync got no backup for account {Account} ({Id}): Success={Success}, Error={Error}",
                    account.Name, account.Id, firstContact?.Success, firstContact?.Error);
                return null;
            }

            var backup = new CustomBackup(firstContact.Backup, cachedContracts, account.Backup);
            if(backup?.Farms is null) {
                logger?.LogWarning("RefreshBackupAsync got a backup with no farms for account {Account} ({Id}); EmptyBackup={Empty}",
                    account.Name, account.Id, backup?.EmptyBackup);
                return null;
            }

            account.Backup = backup;
            return backup;
        }

        // Pulls grade + per-season CXP from get_contract_player_info and applies them: grade via GradeSync
        // (in memory, repacks the account blob) and UserSeasonProgress upserts staged on `db` (NOT saved).
        // Caller persists the account blob and calls SaveChanges. Returns whether the account blob was
        // mutated (grade changed or PromotionTime re-stamped) and therefore needs persisting.
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
            // Update TotalCS and SeasonCS in backup so that CS leaderboard shows all the users 
            if(account.Backup is not null) {
                account.Backup.TotalCS = info.TotalCxp;
                account.Backup.SeasonCS = info.SeasonCxp;
            }

            // get_contract_player_info.Grade is the authoritative current grade. When it differs from
            // LastGrade the player was promoted, so stamp PromotionTime - otherwise GetGrade's
            // "accepted > PromotionTime" guard keeps returning the stale most-recent-contract grade.
            var mutated = GradeSync.ApplyGradeChange(user, account, info.Grade, setPromotionTime: true, guardUnset: true, logger);
            if(!mutated) {
                // LastGrade already matches the API. But if PromotionTime predates the most recent
                // contract (which still carries the old grade), GetGrade would surface that stale grade.
                // Re-stamp PromotionTime so the authoritative grade wins. Heals already-stuck accounts.
                var (backupGrade, accepted) = account.Backup?.GetMostRecentContractGrade()
                    ?? (Ei.Contract.Types.PlayerGrade.GradeUnset, DateTimeOffset.MinValue);
                if(info.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset && backupGrade != info.Grade && accepted > account.PromotionTime) {
                    account.PromotionTime = DateTimeOffset.UtcNow;
                    user.UpdateAccounts();
                    mutated = true;
                    logger.LogInformation("Re-stamped PromotionTime for {User} ({Account}) to keep authoritative grade {Grade}", user.DiscordUsername, account.Name, info.Grade);
                } else {
                    // No grade change, but we still need to call UpdateAccounts to ensure the account blob is repacked with the updated TotalCS and SeasonCS.
                    user.UpdateAccounts();
                    mutated = account.Backup is not null;
                    logger.LogInformation("No grade change for user {User} ({Account}) grade: {Grade}", user.DiscordUsername, account.Name, info.Grade);
                }
            }

            await UpsertSeasonProgress(account.Id, info.SeasonProgress, db, cancellationToken);
            return mutated;
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
