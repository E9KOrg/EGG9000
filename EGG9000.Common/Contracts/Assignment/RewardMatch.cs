using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class RewardMatch {
        // The effective reward filter for a contract: PE is ignored on seasonal contracts (seasonal PE
        // is governed by SeasonalContractsRule; its RewardFilterAfter=true path falls through to the
        // Include tier). Stripping PE can leave the filter empty (PE-only filter): empty means match
        // all, consistent with Matches. Only allocates when PE is actually present and must go.
        public static IReadOnlyList<Ei.RewardType> EffectiveFilter(IReadOnlyList<Ei.RewardType> filter, ContractFacts c) =>
            c.IsSeasonal && filter is { Count: > 0 } && filter.Contains(Ei.RewardType.EggsOfProphecy)
                ? filter.Where(r => r != Ei.RewardType.EggsOfProphecy).ToList()
                : filter;

        // Normalizes a stored/parsed reward filter: strips the "any reward" sentinel, and optionally PE
        // (the V1 new-contract filter never carried PE; the legacy filter did).
        public static List<Ei.RewardType> Sanitize(IEnumerable<Ei.RewardType> source, bool stripPe = false) =>
            (source ?? Enumerable.Empty<Ei.RewardType>())
                .Where(r => r != Ei.RewardType.UnknownReward && (!stripPe || r != Ei.RewardType.EggsOfProphecy))
                .ToList();

        public static bool Matches(GradeRewardFacts grade, IReadOnlyList<Ei.RewardType> filter, int completedGoals) {
            if(filter is null || filter.Count == 0) return true;
            var remaining = grade.GoalRewards.Skip(completedGoals).ToList();
            return filter.Any(r => remaining.Any(g => IsAlias(r, g)));
        }

        public static bool MatchesLast(GradeRewardFacts grade, Ei.RewardType reward) {
            var last = grade.GoalRewards.Count > 0 ? grade.GoalRewards[^1] : (Ei.RewardType?)null;
            return last.HasValue && IsAlias(reward, last.Value);
        }

        private static bool IsAlias(Ei.RewardType selected, Ei.RewardType goal) => selected switch {
            Ei.RewardType.Artifact => goal is Ei.RewardType.Artifact or Ei.RewardType.ArtifactCase,
            Ei.RewardType.PiggyMultiplier => goal is Ei.RewardType.PiggyMultiplier or Ei.RewardType.PiggyLevelBump or Ei.RewardType.PiggyFill,
            _ => goal == selected
        };
    }
}
