using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public sealed record RuleResult(AssignmentRuleId Rule, RuleTier Tier, RuleOutcome Outcome, string Reason);

    public sealed class AssignmentDecision {
        public required bool Assigned { get; init; }
        public required IReadOnlyList<RuleResult> Results { get; init; }
        public string ExclusionReason =>
            Results.FirstOrDefault(r => r.Outcome == RuleOutcome.Exclude)?.Reason;
    }
}
