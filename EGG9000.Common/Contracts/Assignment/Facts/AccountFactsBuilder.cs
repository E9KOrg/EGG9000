using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class AccountFactsBuilder {
        public static AccountFacts Build(
            DBUser user,
            EggIncAccount account,
            Contract contract,
            IEnumerable<Coop> existingCoops,
            UserCsHistoryEntry latestHistory,
            SeasonInfo season,
            IEnumerable<UserSeasonProgress> seasonProgresses) {

            var backup = account.Backup;
            var grade = account.GetGrade();
            var details = contract.Details;
            var coops = existingCoops ?? Enumerable.Empty<Coop>();

            var farmHere = backup?.Farms?.FirstOrDefault(f => f.ContractId == contract.ID);
            var archivedHere = backup?.ArchivedFarms?.FirstOrDefault(f => f.ContractId == contract.ID);
            var completedGoals = (int)(farmHere?.NumGoalsAchieved ?? archivedHere?.NumGoalsAchieved ?? 0);

            var previouslyCompleted = (backup?.Farms?.Any(f => f.Completed && f.ContractId == contract.ID) ?? false)
                || (backup?.ArchivedFarms?.Any(f => f.Completed && f.ContractId == contract.ID) ?? false);
            var completedTwo = (backup?.Farms?.Any(f => f.ContractId == contract.ID && f.NumGoalsAchieved == 2) ?? false)
                || (backup?.ArchivedFarms?.Any(f => f.ContractId == contract.ID && f.NumGoalsAchieved == 2) ?? false);

            var isColleggtible = details.Egg == Ei.Egg.CustomEgg && !string.IsNullOrEmpty(details.CustomEggId);
            var missingColleggtible = isColleggtible && backup != null && backup.GetColleggtibleLevel(details.CustomEggId) < 4;

            return new AccountFacts {
                AccountId = account.Id,
                Grade = grade,
                HasBackup = backup is not null,
                UserDisabled = user.TempDisabled,
                OnBreak = account.OnBreakUntil >= DateTimeOffset.UtcNow,
                HasActiveSubscription = account.HasActiveSubscription(),
                SoulEggs = backup?.SoulEggs ?? 0,
                MaxEggReached = backup is not null ? (int)backup.MaxEggReached : 0,
                AlreadyFarming = backup?.Farms?.Any(f => f.ContractId == contract.ID && f.FarmType == Ei.FarmType.Contract) ?? false,
                AlreadyAssigned = backup is not null && coops.Any(c => c.UserCoopsXrefs.Any(z => z.EggIncId == backup.EggIncId)),
                BoardingGroup = account.GetGroup(contract.cc_only),
                CompletedGoalsOnThisContract = completedGoals,
                PreviouslyCompleted = previouslyCompleted,
                CompletedExactlyTwoGoals = completedTwo,
                MissingColleggtible = missingColleggtible,
                MissingSeasonalPe = SeasonalPeProgress.IsMissing(account.Id, grade, season, seasonProgresses),
                PreviousScoreOnThisContract = latestHistory?.Cxp
            };
        }
    }
}
