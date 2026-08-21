using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;

using Google.Protobuf;

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
        private static readonly TimeSpan CalculatingBufferWindow = TimeSpan.FromHours(1);

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

        public static async Task<CustomBackup> RefreshFullAsync(EggIncAccount account, FrozenSet<Ei.Contract> cachedContracts, DBUser user, ApplicationDbContext db, ILogger logger) {
            var backup = await RefreshBackupAsync(account, cachedContracts, logger);
            if(backup is null) return null;
            await ApplyExtrasAsync(user, account, db, logger);
            return backup;
        }

        // Pulls grade + per-season CXP from get_contract_player_info and applies them: grade via GradeSync
        // (in memory, repacks the account blob) and UserSeasonProgress upserts staged on `db` (NOT saved).
        // Caller persists the account blob and calls SaveChanges. Returns whether the account blob was
        // mutated (grade changed or PromotionTime re-stamped) and therefore needs persisting.
        public static async Task<bool> ApplyExtrasAsync(DBUser user, EggIncAccount account, ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken = default) {
            var info = await FetchExtrasAsync(user, account, logger);
            if(info is null) return false;

            var mutated = ApplyExtras(user, account, info, logger);
            if(info.Status == Ei.ContractPlayerInfo.Types.Status.Complete)
                await UpsertSeasonProgress(account.Id, info.SeasonProgress, db, cancellationToken);
            return mutated;
        }

        // Network-only half of ApplyExtrasAsync. Callers that need to avoid holding a DB connection
        // open across the Egg Inc API call (e.g. batch jobs) can call this first, then ApplyExtras +
        // UpsertSeasonProgress against a short-lived scope once the network round-trip is done.
        public static async Task<Ei.ContractPlayerInfo> FetchExtrasAsync(DBUser user, EggIncAccount account, ILogger logger) {
            var (info, error) = await EggIncApi.GetContractPlayerInfo(account.Id);
            if(info is null) {
                logger.LogWarning("No response getting grade for user {User} ({Account}): {Error}", user.DiscordUsername, account.Name, error);
                return null;
            }
            return info;
        }

        // DB-free half of ApplyExtrasAsync: applies grade + CS changes to the in-memory account blob.
        // Caller still owes UpsertSeasonProgress(account.Id, info.SeasonProgress, db, ...) + persist.
        public static bool ApplyExtras(DBUser user, EggIncAccount account, Ei.ContractPlayerInfo info, ILogger logger) {
            var backupMutated = false;
            if(account.Backup is not null && info.Status == Ei.ContractPlayerInfo.Types.Status.Complete) {
                if(account.Backup.TotalCS != info.TotalCxp || account.Backup.SeasonCS != info.SeasonCxp) {
                    account.Backup.TotalCS = info.TotalCxp;
                    account.Backup.SeasonCS = info.SeasonCxp;
                    backupMutated = true;
                }

                var trimmed = info.Clone();
                trimmed.UnreadEvaluations.Clear();
                trimmed.SeasonProgress.Clear();
                var freshBytes = trimmed.ToByteArray();
                if(!freshBytes.AsSpan().SequenceEqual(account.Backup.LastContractPlayerInfoBytes ?? [])) {
                    account.Backup.LastContractPlayerInfoBytes = freshBytes;
                    backupMutated = true;
                }
            }

            var gradeMutated = info.Status switch {
                Ei.ContractPlayerInfo.Types.Status.Complete => ApplyCompleteGrade(user, account, info, logger),
                Ei.ContractPlayerInfo.Types.Status.Calculating => ApplyCalculatingGrade(user, account, info, logger),
                _ => LogSkippedStatus(user, account, info, logger)
            };

            var mutated = backupMutated || gradeMutated;
            if(mutated) user.UpdateAccounts();
            return mutated;
        }

        private static bool ApplyCompleteGrade(DBUser user, EggIncAccount account, Ei.ContractPlayerInfo info, ILogger logger) {
            var mutated = GradeSync.ApplyGradeChange(user, account, info.Grade, setPromotionTime: true, guardUnset: true, logger);

            var pendingCleared = false;
            if(account.PendingGrade.HasValue) {
                account.PendingGrade = null;
                account.PendingGradeSince = default;
                pendingCleared = true;
            }

            if(mutated) return true;

            var (backupGrade, accepted) = account.Backup?.GetMostRecentContractGrade()
                ?? (Ei.Contract.Types.PlayerGrade.GradeUnset, DateTimeOffset.MinValue);
            if(info.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset && backupGrade != info.Grade && accepted > account.PromotionTime) {
                account.PromotionTime = DateTimeOffset.UtcNow;
                logger.LogInformation("Re-stamped PromotionTime for {User} ({Account}) to keep authoritative grade {Grade}", user.DiscordUsername, account.Name, info.Grade);
                return true;
            }

            logger.LogInformation("No grade change for user {User} ({Account}) grade: {Grade}", user.DiscordUsername, account.Name, info.Grade);
            return pendingCleared;
        }

        private static bool ApplyCalculatingGrade(DBUser user, EggIncAccount account, Ei.ContractPlayerInfo info, ILogger logger) {
            if(!GradeSync.ShouldUpdateGrade(account.LastGrade, info.Grade, guardUnset: true)) {
                if(!account.PendingGrade.HasValue) return false;
                account.PendingGrade = null;
                account.PendingGradeSince = default;
                return true;
            }

            if(account.PendingGrade != info.Grade) {
                account.PendingGrade = info.Grade;
                account.PendingGradeSince = DateTimeOffset.UtcNow;
                logger.LogInformation("Buffering CALCULATING grade candidate {Grade} for {User} ({Account}), current LastGrade {Current}",
                    info.Grade, user.DiscordUsername, account.Name, account.LastGrade);
                return true;
            }

            if(DateTimeOffset.UtcNow - account.PendingGradeSince < CalculatingBufferWindow) {
                return false;
            }

            logger.LogInformation("CALCULATING grade candidate {Grade} for {User} ({Account}) persisted past the buffer window, accepting anyway",
                info.Grade, user.DiscordUsername, account.Name);
            GradeSync.ApplyGradeChange(user, account, info.Grade, setPromotionTime: true, guardUnset: true, logger);
            account.PendingGrade = null;
            account.PendingGradeSince = default;
            return true;
        }

        private static bool LogSkippedStatus(DBUser user, EggIncAccount account, Ei.ContractPlayerInfo info, ILogger logger) {
            logger.LogTrace("Skipping non-final grade ({Status}) for user {User} ({Account})", info.Status, user.DiscordUsername, account.Name);
            return false;
        }

        public static async Task UpsertSeasonProgress(string eggIncId, IEnumerable<Ei.ContractPlayerInfo.Types.SeasonProgress> seasonProgress, ApplicationDbContext db, CancellationToken cancellationToken) {
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
