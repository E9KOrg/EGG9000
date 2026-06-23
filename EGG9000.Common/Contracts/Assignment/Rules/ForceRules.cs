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
}
