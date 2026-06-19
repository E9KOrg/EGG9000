using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class AssignmentRuleSet {
        // Order matches the legacy OrganizeCoops filter chain (grade -> ... -> already assigned).
        public static readonly IReadOnlyList<IAssignmentRule> Gate = new List<IAssignmentRule> {
            new GradeUnsetRule(), new BackupMissingRule(), new UserDisabledRule(), new OnBreakRule(),
            new NoSubscriptionRule(), new InsufficientSoulEggsRule(), new EggLockedRule(),
            new AlreadyFarmingRule(), new AlreadyAssignedRule()
        };

        public static readonly IReadOnlyList<IAssignmentRule> Force = new List<IAssignmentRule> {
            new ColleggtibleForceRule(), new SeasonalPeForceRule()
        };

        // Reward filters run before the previously-completed check (matches legacy order).
        public static readonly IReadOnlyList<IAssignmentRule> Include = new List<IAssignmentRule> {
            new NewRewardFilterRule(), new LegacyRewardFilterRule(), new PreviouslyCompletedRule()
        };

        public static IReadOnlyList<IAssignmentRule> All => Gate.Concat(Force).Concat(Include).ToList();
    }
}
