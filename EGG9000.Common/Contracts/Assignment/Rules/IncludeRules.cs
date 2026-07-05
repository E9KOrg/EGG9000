using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

namespace EGG9000.Common.Contracts.Assignment.Rules {
    static internal class IncludeRuleHelpers {
        public static GradeRewardFacts GradeOrNull(ContractFacts c, Ei.Contract.Types.PlayerGrade grade) =>
            c.GradeRewards != null && c.GradeRewards.TryGetValue(grade, out var g) ? g : null;
    }

    // One reward filter for all contracts (the new/legacy split is gone). Empty filter = match all.
    // PE may be in the filter; in practice only leggacy contracts still carry a PE goal reward
    // (modern contracts grant PE via season progress instead). PE handling for seasonal contracts is
    // centralized in RewardMatch.EffectiveFilter.
    public sealed class RewardFilterRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.RewardFilter;
        public RuleTier Tier => RuleTier.Include;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) {
            var grade = IncludeRuleHelpers.GradeOrNull(c, f.Grade);
            if(grade is null) return RuleOutcome.Exclude;
            return RewardMatch.Matches(grade, RewardMatch.EffectiveFilter(s.RewardFilter, c), f.CompletedGoalsOnThisContract)
                ? RuleOutcome.Pass : RuleOutcome.Exclude;
        }
        public string Describe(RuleOutcome o) => "Rewards not selected";
    }

    // Ports CheckOnPreviousComplete. Only applies when the account previously completed the contract
    // (full completion) or completed exactly two of three goals on a 2->3 contract. Colleggtible bypass
    // is handled earlier by ColleggtibleForceRule (Force tier short-circuits the Include tier).
    public sealed class PreviouslyCompletedRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.RedoCompleted;
        public RuleTier Tier => RuleTier.Include;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) {
            if(!f.PreviouslyCompleted && !f.CompletedExactlyTwoGoals) return RuleOutcome.Pass;

            var redo = s.Redo ?? new RedoRule();

            // Seasonal carve-out: redo legacies but skip seasonal replays.
            if(redo.ExcludeSeasonal && c.IsSeasonal) return RuleOutcome.Exclude;

            if(redo.Mode == RedoLeggacyOption.YesAll) return RuleOutcome.Pass;
            if(redo.Mode == RedoLeggacyOption.YesNoUltra && !c.IsUltra) return RuleOutcome.Pass;
            if(redo.Mode == RedoLeggacyOption.YesThreshold && (f.PreviousScoreOnThisContract ?? 0) <= redo.ScoreThreshold) return RuleOutcome.Pass;
            if(redo.Mode == RedoLeggacyOption.YesOtherAccountMatch && f.SiblingMatchProvisionalInclude) return RuleOutcome.Pass;

            var grade = IncludeRuleHelpers.GradeOrNull(c, f.Grade);
            if(c.HadTwoRewards && grade != null && grade.GoalRewards.Count == 3 && f.CompletedExactlyTwoGoals) {
                if(!s.TwoToThree) return RuleOutcome.Exclude;
                // Same PE-on-seasonal handling as RewardFilterRule so PE is treated consistently.
                var filter = RewardMatch.EffectiveFilter(s.RewardFilter, c);
                if(filter == null || filter.Count == 0) return RuleOutcome.Pass;
                foreach(var r in filter)
                    if(RewardMatch.MatchesLast(grade, r)) return RuleOutcome.Pass;
                return RuleOutcome.Exclude;
            }

            if(redo.Mode == RedoLeggacyOption.No && f.PreviouslyCompleted) return RuleOutcome.Exclude;

            return f.PreviouslyCompleted ? RuleOutcome.Exclude : RuleOutcome.Pass;
        }
        public string Describe(RuleOutcome o) => "Previously completed";
    }
}
