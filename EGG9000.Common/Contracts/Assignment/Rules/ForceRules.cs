namespace EGG9000.Common.Contracts.Assignment {
    public sealed class ColleggtibleForceRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.MissingColleggtible;
        public RuleTier Tier => RuleTier.Force;
        public bool AppliesTo(ContractFacts c) => c.IsColleggtible;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) {
            var rule = s.Get(PermanentRewardKind.Colleggtible);
            if(rule.Mode == ForceMode.AssignIfMissing && f.MissingColleggtible) return RuleOutcome.ForceInclude;
            return RuleOutcome.NotApplicable;
        }
        public string Describe(RuleOutcome o) => "Missing colleggtible (force assign)";
    }

    // Seasonal PE only ever adds assignment. No skip/Never path (never approved).
    public sealed class SeasonalPeForceRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.MissingSeasonalPe;
        public RuleTier Tier => RuleTier.Force;
        public bool AppliesTo(ContractFacts c) => c.IsSeasonal;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) {
            var rule = s.Get(PermanentRewardKind.SeasonalPe);
            switch(rule.Mode) {
                case ForceMode.AssignIfMissing:
                    return f.MissingSeasonalPe ? RuleOutcome.ForceInclude : RuleOutcome.NotApplicable;
                case ForceMode.BelowThreshold:
                    return f.MissingSeasonalPe && (f.PreviousScoreOnThisContract ?? 0) < (rule.CsFloor ?? 0)
                        ? RuleOutcome.ForceInclude : RuleOutcome.NotApplicable;
                default:
                    return RuleOutcome.NotApplicable;
            }
        }
        public string Describe(RuleOutcome o) => "Missing seasonal PE (force assign)";
    }
}
