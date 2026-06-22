using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment.Diagnostics {
    // Frozen verbatim reconstruction of the pre-rewrite OrganizeCoops filter chain.
    // Reads the OLD scalar keys on EggIncAccount (AutoRegisterRewards, RedoLeggacySelection,
    // SeasonalPeOption, ...). Diagnostics-only: never referenced by live assignment code.
    // The random coop-packing (_SortUsersIntoDay1Coops) is intentionally excluded - only the
    // per-account include/exclude DECISION is reproduced.
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

            FilterAccounts(accounts, excluded, x => x.Account.GetGrade() != Ei.Contract.Types.PlayerGrade.GradeUnset, "Grade is unset");

            FilterAccounts(accounts, excluded, x => x.Account.Backup is not null, "Backup is empty");

            FilterAccounts(accounts, excluded, x => !x.User.TempDisabled, "User disabled");

            FilterAccounts(accounts, excluded, x => x.Account.OnBreakUntil < DateTimeOffset.UtcNow, "On break");

            FilterAccounts(accounts, excluded, x => !contract.Details.CcOnly || x.Account.HasActiveSubscription(), "Doesn't have subscription");

            FilterAccounts(accounts, excluded, x => x.Account.Backup.SoulEggs >= 1000, "< 1k soul eggs");

            FilterAccounts(accounts, excluded, x =>
                x.Account.Backup.MaxEggReached == 0 || (int)x.Account.Backup.MaxEggReached >= (int)contract.Details.Egg || (int)contract.Details.Egg >= 100, "Egg not unlocked");

            FilterAccounts(accounts, excluded, x => !x.Account.Backup.Farms.Any(y => y.ContractId == contract.ID && y.FarmType == Ei.FarmType.Contract), "Already In Co-op");

            FilterAccounts(accounts, excluded, x => !existingCoops.Any(y => y.UserCoopsXrefs.Any(z => z.EggIncId == x.Account.Backup.EggIncId)), "Already assigned a co-op");

            FilterAccounts(accounts, excluded, x => {
                if(dbGuild is not null && dbGuild.DisableBG) return true;

                if(UncompleteColleggtibleBypass(x, contract)) return true;

                if(x.Account.GetGrade() == Ei.Contract.Types.PlayerGrade.GradeUnset) return false;

                var gradeSpec = contract.Details.GradeSpecs.First(y => y.Grade == x.Account.GetGrade());
                if(gradeSpec is null || gradeSpec.Grade != x.Account.GetGrade()) return false;

                var leggacyRegisterRewards = new List<Ei.RewardType>();
                if(x.Account.LeggacyAutoRegisterRewards is null || x.Account.LeggacyAutoRegisterRewards.Count == 0) leggacyRegisterRewards = x.Account.AutoRegisterRewards;
                else leggacyRegisterRewards = x.Account.LeggacyAutoRegisterRewards;

                var registerRewards = contract.Details.Leggacy ? leggacyRegisterRewards : x.Account.AutoRegisterRewards;
                registerRewards ??= [];

                var completedRewards = x.Account.Backup.Farms.FirstOrDefault(y => y.ContractId == contract.ID)?.NumGoalsAchieved ?? x.Account.Backup.ArchivedFarms.FirstOrDefault(y => y.ContractId == contract.ID)?.NumGoalsAchieved ?? 0;

                return registerRewards.Count == 0 || registerRewards.Any(r => DBUser.MatchRewards(gradeSpec, r, completedRewards));
            }, "Rewards not selected");

            if (contractSeason != null && !dbGuild.DisableBG) {
                FilterAccounts(accounts, excluded, x => ShouldIncludeForSeasonalPe(
                    x.Account,
                    x.Account.GetGrade(),
                    contractSeason,
                    seasonProgresses ?? [],
                    x.UserCsHistoryEntry?.Cxp),
                    "Seasonal PE not needed");
            }

            FilterAccounts(accounts, excluded, x => CheckOnPreviousComplete(dbGuild, x, contract, accounts.Where(a => a.User == x.User && a.Account.Id != x.Account.Id).ToList()), "Previously completed");

            var results = new List<LegacyAccountResult>();
            foreach(var survivor in accounts) {
                results.Add(new LegacyAccountResult(survivor.Account.Id, survivor.User.DiscordId, true, null));
            }
            foreach(var (reason, account) in excluded) {
                results.Add(new LegacyAccountResult(account.Account.Id, account.User.DiscordId, false, reason));
            }
            return results;
        }

        public static bool ShouldIncludeForSeasonalPe(
            EggIncAccount account,
            Ei.Contract.Types.PlayerGrade grade,
            SeasonInfo contractSeason,
            List<UserSeasonProgress> seasonProgresses,
            double? contractScore) {

            if (account.SeasonalPeOption == SeasonalPeOption.NotSet)
                return true;

            if (account.SeasonalPeOption == SeasonalPeOption.DontAssign)
                return false;

            if (account.SeasonalPeOption == SeasonalPeOption.AlwaysAssignIfMissing) {
                var progress = seasonProgresses.FirstOrDefault(x => x.EggIncId == account.Id && x.SeasonId == contractSeason.Id);
                var seasonGrade = progress != null ? (Ei.Contract.Types.PlayerGrade)progress.StartingGrade : grade;
                var maxPe = contractSeason.GetMaxPe(seasonGrade);
                if (maxPe == 0) return true;
                var earnedPe = contractSeason.GetPeEarned(seasonGrade, progress?.TotalCxp ?? 0);
                return earnedPe < maxPe;
            }

            if (account.SeasonalPeOption == SeasonalPeOption.AssignIfBelowThreshold) {
                return (contractScore ?? 0) < account.SeasonalPeThreshold;
            }

            return true;
        }

        private static List<Ei.RewardType> GetRegisterRewards(Guild dbGuild, UserByAccount x, Contract contract) {
            if(dbGuild is not null && dbGuild.DisableBG) return null;

            if(x.Account.GetGrade() == Ei.Contract.Types.PlayerGrade.GradeUnset) return null;

            var gradeSpec = contract.Details.GradeSpecs.First(y => y.Grade == x.Account.GetGrade());
            if(gradeSpec is null || gradeSpec.Grade != x.Account.GetGrade()) return null;

            var leggacyRegisterRewards = new List<Ei.RewardType>();
            if(x.Account.LeggacyAutoRegisterRewards is null || x.Account.LeggacyAutoRegisterRewards.Count == 0) leggacyRegisterRewards = x.Account.AutoRegisterRewards;
            else leggacyRegisterRewards = x.Account.LeggacyAutoRegisterRewards;

            var registerRewards = contract.Details.Leggacy ? leggacyRegisterRewards : x.Account.AutoRegisterRewards;
            registerRewards ??= [];

            return registerRewards;
        }

        private static void FilterAccounts(List<UserByAccount> accounts, List<(string, UserByAccount)> excluded, Func<UserByAccount, bool> includeInCoopFilter, string reasonNotIncluded) {
            excluded.AddRange(accounts.Where(x => !includeInCoopFilter(x)).Select(x => (reasonNotIncluded, x)));
            accounts.RemoveAll(x => !includeInCoopFilter(x));
        }

        private static bool MatchGroup(EggIncAccount a1, EggIncAccount a2, Contract c) {
            return(a1.GetGroup(c.cc_only).Equals(a2.GetGroup(c.cc_only)));
        }
        private static bool MatchGrade(EggIncAccount a1, EggIncAccount a2, Contract c) {
            return a1.GetGrade().Equals(a2.GetGrade()) || (c.cc_only && a1.HasActiveSubscription() && a2.HasActiveSubscription());
        }

        private static bool UncompleteColleggtibleBypass(UserByAccount x, Contract contract) {
            if(x.Account.DoUnfinishedCollegtibles && contract.Details.Egg == Ei.Egg.CustomEgg && contract.Details.CustomEggId != "") {
                if(x.Account.Backup.GetColleggtibleLevel(contract.Details.CustomEggId) < 4) return true;
            }
            return false;
        }

        private static bool CheckOnPreviousComplete(Guild dbGuild, UserByAccount x, Contract contract, List<UserByAccount> otherAccounts) {
            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.YesAll)
                return true;

            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.YesNoUltra && !contract.cc_only)
                return true;

            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.YesThreshold && (x.UserCsHistoryEntry?.Cxp ?? 0) <= x.Account.RedoScoreThreshold)
                return true;

            if(otherAccounts.Count > 0 && x.Account.RedoLeggacySelection == RedoLeggacyOption.YesOtherAccountMatch && otherAccounts.Any(ua =>
                ua.Account.Id != x.Account.Id &&
                MatchGrade(ua.Account, x.Account, contract) &&
                MatchGroup(ua.Account, x.Account, contract) &&
                CheckOnPreviousComplete(dbGuild, ua, contract, [])
            )) return true;

            if(UncompleteColleggtibleBypass(x, contract)) return true;

            if(contract.HadTwoRewards && contract.Details.GradeSpecs[((int)x.Account.GetGrade()) - 1].Goals.Count == 3) {
                var completedTwoRewards = (x.Account.Backup.Farms.Any(f => f.ContractId == contract.ID && f.NumGoalsAchieved == 2) || x.Account.Backup.ArchivedFarms.Any(f => f.ContractId == contract.ID && f.NumGoalsAchieved == 2));
                if(completedTwoRewards && !x.Account.DoTwoToThreeContracts) {
                    return false;
                } else if(completedTwoRewards && x.Account.DoTwoToThreeContracts) {
                    var gradeSpec = contract.Details.GradeSpecs.First(y => y.Grade == x.Account.GetGrade());
                    if(gradeSpec is null || gradeSpec.Grade != x.Account.GetGrade()) return false;

                    var registerRewards = GetRegisterRewards(dbGuild, x, contract);

                    if(registerRewards is null) return false;

                    return registerRewards.Count == 0 || registerRewards.Any(r => DBUser.MatchLastReward(gradeSpec, r));
                }
            }

            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.No
                && (
                    x.Account.Backup.Farms.Any(f => f.Completed && f.ContractId == contract.ID) ||
                    x.Account.Backup.ArchivedFarms.Any(f => f.Completed && f.ContractId == contract.ID)
                ))
                return false;

            return (!x.Account.Backup.Farms.Any(f => f.ContractId == contract.ID && f.Completed) && !x.Account.Backup.ArchivedFarms.Any(f => f.ContractId == contract.ID && f.Completed));
        }
    }
}
