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

            var newResults = new Dictionary<string, (bool assigned, string reason, ulong discordId, EggIncAccount account)>();
            foreach(var user in users) {
                var inputs = new List<(AccountFacts facts, AssignmentSettings settings)>();
                var byFacts = new Dictionary<AccountFacts, EggIncAccount>();
                foreach(var account in user.EggIncAccounts) {
                    var latestHistory = csHistoryEntries.Where(x => x.EggIncId == account.Id).MaxBy(x => x.Created);
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

        // The approved ruling deviations: the new engine no longer skips seasonal-PE players the way
        // the old SeasonalPeOption did. True when the contract is seasonal AND the old option would
        // have excluded a player the new engine now lets through.
        public static bool IsExpectedSeasonalDeviation(EggIncAccount account, SeasonInfo contractSeason, List<UserSeasonProgress> seasonProgresses) {
            if(contractSeason is null) return false;

            return ClassifySeasonalDeviation(
                account.SeasonalPeOption,
                () => SeasonalPeProgress.IsMissing(account.Id, account.GetGrade(), contractSeason, seasonProgresses));
        }

        // Pure classifier, extracted so it is unit-testable without a full DBUser/Backup graph.
        // missingPe is evaluated lazily because it is only consulted for the assign-if-missing options.
        public static bool ClassifySeasonalDeviation(SeasonalPeOption option, System.Func<bool> missingPe) {
            if(option == SeasonalPeOption.DontAssign) return true;

            if(option == SeasonalPeOption.AlwaysAssignIfMissing || option == SeasonalPeOption.AssignIfBelowThreshold) {
                return !missingPe();
            }

            return false;
        }
    }
}
