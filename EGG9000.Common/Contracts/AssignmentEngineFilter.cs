using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts {
    // LIVE assignment via the new rule engine (Assignment/). Drop-in replacement for
    // LegacyAssignmentFilter.ApplyFilters: same signature, same in-place contract (filters `accounts`
    // to the keep-set and records each removed account's reason into `excluded`). The two engines were
    // validated to parity by AssignmentParityChecker before this became the live path; the only intended
    // differences are the v2 seasonal redesign and reward-filter collapse.
    public static class AssignmentEngineFilter {
        public static void ApplyFilters(
            List<UserByAccount> accounts,
            List<(string reason, UserByAccount account)> excluded,
            Contract contract,
            List<Coop> existingCoops,
            Guild dbGuild,
            SeasonInfo contractSeason,
            List<UserSeasonProgress> seasonProgresses) {

            var coops = existingCoops ?? [];
            var progresses = seasonProgresses ?? [];
            var contractFacts = ContractFactsBuilder.Build(contract, contractSeason);
            var filtersDisabled = dbGuild?.DisableBG ?? false;
            var forbidden = dbGuild?.RuleOverrides;

            var removed = new List<(string reason, UserByAccount account)>();
            var keep = new HashSet<UserByAccount>();

            foreach(var userGroup in accounts.GroupBy(x => x.User)) {
                var members = userGroup.ToList();
                var inputs = new List<(AccountFacts facts, AssignmentSettings settings)>();
                var byFacts = new Dictionary<AccountFacts, UserByAccount>();

                foreach(var entry in members) {
                    var facts = AccountFactsBuilder.Build(
                        entry.User, entry.Account, contract, coops, entry.UserCsHistoryEntry, contractSeason, progresses);
                    inputs.Add((facts, entry.Account.Assignment ?? new AssignmentSettings()));
                    byFacts[facts] = entry;
                }

                foreach(var (facts, decision) in AssignmentEvaluator.EvaluateUser(inputs, contractFacts, forbidden, filtersDisabled)) {
                    var entry = byFacts[facts];
                    if(decision.Assigned) keep.Add(entry);
                    else removed.Add((decision.ExclusionReason, entry));
                }
            }

            excluded.AddRange(removed);
            accounts.RemoveAll(x => !keep.Contains(x));
        }
    }
}
