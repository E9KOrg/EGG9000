using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment.Diagnostics {
    // Before/after validator: runs the frozen legacy decision and the new engine over identical
    // inputs and reports per-account mismatches. Read-only - never touches the DB.
    public static class AssignmentParityChecker {
        public sealed record ParityReport(string ContractId, int Total, int Matched, List<ParityMismatch> Mismatches);

        public sealed record ParityMismatch(string EggIncId, ulong DiscordId, bool LegacyAssigned, bool NewAssigned,
            string LegacyReason, string NewReason, bool ExpectedSeasonalDeviation);

        public static ParityReport Compare(
            List<DBUser> users, Contract contract, List<Coop> existingCoops, Guild dbGuild,
            SeasonInfo contractSeason, List<UserSeasonProgress> seasonProgresses, List<UserCsHistoryEntry> csHistory) {

            var csHistoryEntries = csHistory ?? [];
            var progresses = seasonProgresses ?? [];
            var coops = existingCoops ?? [];

            var legacy = LegacyAssignmentDecision.Filter(users, contract, coops, dbGuild, contractSeason, progresses, csHistoryEntries);
            var legacyByAccount = legacy.ToDictionary(r => r.EggIncId);

            var contractFacts = ContractFactsBuilder.Build(contract, contractSeason);
            var filtersDisabled = dbGuild?.DisableBG ?? false;
            var forbidden = dbGuild?.RuleOverrides;

            // Latest CS-history entry per account, built once (avoids an O(n^2) Where().MaxBy() per account).
            var latestHistoryByAccount = csHistoryEntries
                .GroupBy(x => x.EggIncId)
                .ToDictionary(g => g.Key, g => g.MaxBy(x => x.Created));

            var newResults = new Dictionary<string, (bool assigned, string reason, ulong discordId, EggIncAccount account)>();
            foreach(var user in users) {
                var inputs = new List<(AccountFacts facts, AssignmentSettings settings)>();
                var byFacts = new Dictionary<AccountFacts, EggIncAccount>();
                foreach(var account in user.EggIncAccounts) {
                    latestHistoryByAccount.TryGetValue(account.Id, out var latestHistory);
                    var facts = AccountFactsBuilder.Build(user, account, contract, coops, latestHistory, contractSeason, progresses);
                    inputs.Add((facts, account.Assignment ?? new AssignmentSettings()));
                    byFacts[facts] = account;
                }

                var decisions = AssignmentEvaluator.EvaluateUser(inputs, contractFacts, forbidden, filtersDisabled);
                foreach(var (facts, decision) in decisions) {
                    var account = byFacts[facts];
                    newResults[account.Id] = (decision.Assigned, decision.ExclusionReason, user.DiscordId, account);
                }
            }

            var mismatches = new List<ParityMismatch>();
            var total = 0;
            foreach(var (eggIncId, newResult) in newResults) {
                total++;
                if(!legacyByAccount.TryGetValue(eggIncId, out var legacyResult)) continue;
                if(legacyResult.Assigned == newResult.assigned) continue;

                var expectedSeasonal = IsExpectedSeasonalDeviation(newResult.account, contractSeason, progresses);
                mismatches.Add(new ParityMismatch(
                    eggIncId, newResult.discordId,
                    legacyResult.Assigned, newResult.assigned,
                    legacyResult.Reason, newResult.reason,
                    expectedSeasonal));
            }

            return new ParityReport(contract.ID, total, total - mismatches.Count, mismatches);
        }

        // By-design seasonal deviations under v2 (mandatory Seasonal Contracts, migrated from the old
        // option). True when a seen old-vs-new mismatch stems from the intended seasonal redesign, not a
        // regression. The reward-filter collapse / PE-dropped diffs are intentionally NOT flagged here -
        // those are rarer and left as "unexpected" for human review.
        public static bool IsExpectedSeasonalDeviation(EggIncAccount account, SeasonInfo contractSeason, List<UserSeasonProgress> seasonProgresses) {
            if(contractSeason is null) return false;

            // New-only seasonal capabilities have no old-key equivalent, and the lossy dual-write cannot
            // represent them, so any seasonal diff they cause is by-design (not a regression):
            //   - AlwaysAssign keeps force-assigning even after PE (dual-writes to AlwaysAssignIfMissing,
            //     which the old logic excludes once PE is earned).
            //   - RewardFilterAfter changes the post-condition behavior the old option cannot express.
            var seasonal = account.Assignment?.Seasonal;
            if(seasonal != null && (seasonal.Mode == SeasonalMode.AlwaysAssign || seasonal.RewardFilterAfter))
                return true;

            return ClassifySeasonalDeviation(
                account.SeasonalPeOption,
                () => SeasonalPeProgress.IsMissing(account.Id, account.GetGrade(), contractSeason, seasonProgresses));
        }

        // Pure classifier, unit-testable without a DBUser/Backup graph. Migration maps the old option to
        // a v2 SeasonalRule (DontAssign/AlwaysAssignIfMissing/NotSet -> UntilPeEarned after=false;
        // AssignIfBelowThreshold -> UntilCsGoal). On a seasonal contract the intended divergences are:
        //   - DontAssign: old always excluded; new force-assigns while PE is still missing.
        //   - NotSet: old always assigned; new excludes once PE is earned (mandatory stop-after-PE).
        // AlwaysAssignIfMissing and AssignIfBelowThreshold map 1:1 and produce no divergence.
        public static bool ClassifySeasonalDeviation(SeasonalPeOption option, System.Func<bool> missingPe) {
            return option switch {
                SeasonalPeOption.DontAssign => missingPe(),
                SeasonalPeOption.NotSet => !missingPe(),
                _ => false
            };
        }
    }
}
