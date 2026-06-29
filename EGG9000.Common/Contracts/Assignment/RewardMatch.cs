using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class RewardMatch {
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
