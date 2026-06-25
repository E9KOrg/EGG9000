using EGG9000.Common.Database.Entities;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class ContractFactsBuilder {
        // season is non-null only when the contract belongs to a PE season (mirrors the legacy
        // "contractSeason != null" gate that decides whether seasonal-PE logic runs at all).
        public static ContractFacts Build(Contract contract, SeasonInfo season) {
            var details = contract.Details;
            var isColleggtible = details.Egg == Ei.Egg.CustomEgg && !string.IsNullOrEmpty(details.CustomEggId);

            var gradeRewards = new Dictionary<Ei.Contract.Types.PlayerGrade, GradeRewardFacts>();
            foreach(var gs in details.GradeSpecs)
                gradeRewards[gs.Grade] = new GradeRewardFacts { GoalRewards = gs.Goals.Select(g => g.RewardType).ToList() };

            return new ContractFacts {
                ContractId = contract.ID,
                IsLegacy = details.Leggacy,
                IsSeasonal = season != null,
                IsUltra = contract.cc_only,
                IsColleggtible = isColleggtible,
                ColleggtibleEggId = details.CustomEggId,
                HadTwoRewards = contract.HadTwoRewards,
                Egg = (int)details.Egg,
                SeasonId = contract.SeasonId,
                GradeRewards = gradeRewards
            };
        }
    }
}
