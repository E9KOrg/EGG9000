using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Contracts.Assignment.Diagnostics {
    // Inline shadow: during a real assignment run, evaluates the new engine against the live old logic
    // on identical inputs and persists every per-account mismatch to ShadowAssignmentDiffs. Fully
    // isolated from assignment - any failure is logged and swallowed; the live assignment is never
    // affected. Writes nothing when the two agree.
    public static class AssignmentShadowRecorder {
        public static async Task RecordAsync(
            ApplicationDbContext db,
            List<DBUser> users,
            Contract contract,
            List<Coop> existingCoops,
            Guild dbGuild,
            SeasonInfo contractSeason,
            List<UserSeasonProgress> seasonProgresses,
            List<UserCsHistoryEntry> csHistory,
            ILogger logger,
            CancellationToken cancellationToken = default) {

            try {
                var report = AssignmentParityChecker.Compare(users, contract, existingCoops, dbGuild, contractSeason, seasonProgresses, csHistory);
                if(report.Mismatches.Count == 0)
                    return;

                var now = DateTimeOffset.UtcNow;
                var guildId = dbGuild?.Id ?? 0;
                var rows = new List<ShadowAssignmentDiff>(report.Mismatches.Count);
                foreach(var m in report.Mismatches) {
                    rows.Add(new ShadowAssignmentDiff {
                        Id = Guid.NewGuid(),
                        CreatedAt = now,
                        ContractId = contract.ID,
                        GuildId = guildId,
                        EggIncId = m.EggIncId,
                        DiscordId = m.DiscordId,
                        LiveAssigned = m.LegacyAssigned,
                        ShadowAssigned = m.NewAssigned,
                        LiveReason = m.LegacyReason,
                        ShadowReason = m.NewReason,
                        ExpectedSeasonalDeviation = m.ExpectedSeasonalDeviation
                    });
                }

                db.ShadowAssignmentDiffs.AddRange(rows);
                await db.SaveChangesAsync(cancellationToken);

                var unexpected = rows.FindAll(r => !r.ExpectedSeasonalDeviation).Count;
                logger?.LogInformation("Shadow assignment for {contract}: {total} mismatches ({unexpected} unexpected, {expected} seasonal-deviation)",
                    contract.ID, rows.Count, unexpected, rows.Count - unexpected);
            } catch(Exception ex) {
                logger?.LogWarning(ex, "Shadow assignment recording failed for {contract}; live assignment unaffected", contract.ID);
            }
        }
    }
}
