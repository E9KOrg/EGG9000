using EGG9000.Common.Database.Entities;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment.Diagnostics {
    // Flat per-account form of the old assignment decision, used by the parity checker and the inline
    // shadow comparison. Delegates to the canonical LegacyAssignmentFilter so there is one old-logic
    // implementation shared with live assignment. The random coop-packing is intentionally excluded.
    public static class LegacyAssignmentDecision {
        public sealed record LegacyAccountResult(string EggIncId, ulong DiscordId, bool Assigned, string Reason);

        public static List<LegacyAccountResult> Filter(
            List<DBUser> users, Contract contract, List<Coop> existingCoops, Guild dbGuild,
            SeasonInfo contractSeason, List<UserSeasonProgress> seasonProgresses, List<UserCsHistoryEntry> csHistory) {

            var userCsHistoryEntries = csHistory ?? [];
            var excluded = new List<(string reason, UserByAccount account)>();

            var accounts = users
                .SelectMany(u => u.EggIncAccounts.Select(a => new UserByAccount {
                    Account = a,
                    User = u,
                    UserCsHistoryEntry = userCsHistoryEntries.Where(x => x.EggIncId == a.Id).MaxBy(x => x.Created),
                    Group = a.GetGroup(contract.Details.CcOnly),
                    RoleId = 0
                })).ToList();

            LegacyAssignmentFilter.ApplyFilters(accounts, excluded, contract, existingCoops, dbGuild, contractSeason, seasonProgresses);

            var results = new List<LegacyAccountResult>();
            foreach(var survivor in accounts)
                results.Add(new LegacyAccountResult(survivor.Account.Id, survivor.User.DiscordId, true, null));
            foreach(var (reason, account) in excluded)
                results.Add(new LegacyAccountResult(account.Account.Id, account.User.DiscordId, false, reason));
            return results;
        }
    }
}
