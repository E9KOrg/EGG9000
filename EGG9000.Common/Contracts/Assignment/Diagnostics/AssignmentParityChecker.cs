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

            var newResults = new Dictionary<string, (bool assigned, string reason, ulong discordId, bool seasonalDecisive)>();
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
                    newResults[account.Id] = (decision.Assigned, decision.ExclusionReason, user.DiscordId, IsSeasonalDecisive(decision));
                }
            }

            var mismatches = new List<ParityMismatch>();
            var total = 0;
            foreach(var (eggIncId, newResult) in newResults) {
                total++;
                if(!legacyByAccount.TryGetValue(eggIncId, out var legacyResult)) continue;
                if(legacyResult.Assigned == newResult.assigned) continue;

                mismatches.Add(new ParityMismatch(
                    eggIncId, newResult.discordId,
                    legacyResult.Assigned, newResult.assigned,
                    legacyResult.Reason, newResult.reason,
                    newResult.seasonalDecisive));
            }

            return new ParityReport(contract.ID, total, total - mismatches.Count, mismatches);
        }

        // A mismatch is a by-design seasonal deviation when the new engine's decision was DRIVEN by the
        // SeasonalContractsRule - i.e. that rule short-circuited the evaluation with a decisive outcome:
        //   - ForceInclude: mandatory seasonal assignment (until PE earned / CS goal) overriding the old
        //     reward-filter or previously-completed exclusion. This is the v2 anti-dodge intent.
        //   - Exclude: seasonal goal already met, so the new engine stops assigning (no old equivalent).
        // The old logic had no mandatory-seasonal tier, so any time this rule is decisive the divergence
        // is the intended redesign, not a regression. When the seasonal rule is NOT decisive, the diff
        // stems from somewhere else and stays flagged as unexpected for human review.
        public static bool IsSeasonalDecisive(AssignmentDecision decision) {
            return decision.Results.Any(r =>
                r.Rule == AssignmentRuleId.SeasonalContracts &&
                (r.Outcome == RuleOutcome.ForceInclude || r.Outcome == RuleOutcome.Exclude));
        }
    }
}
