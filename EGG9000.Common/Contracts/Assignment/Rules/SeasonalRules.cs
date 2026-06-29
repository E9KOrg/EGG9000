namespace EGG9000.Common.Contracts.Assignment {
    // Seasonal Contracts rule (Force tier). Mandatory: there is no off. Force-assigns until the chosen
    // condition is met; once met, RewardFilterAfter decides whether the normal reward filter governs
    // (NotApplicable -> fall through) or assignment stops for the season (Exclude).
    public sealed class SeasonalContractsRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.SeasonalContracts;
        public RuleTier Tier => RuleTier.Force;
        public bool AppliesTo(ContractFacts c) => c.IsSeasonal;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) {
            var r = s.Seasonal ?? new SeasonalRule();
            switch(r.Mode) {
                case SeasonalMode.AlwaysAssign:
                    return RuleOutcome.ForceInclude;
                case SeasonalMode.UntilPeEarned:
                    if(f.MissingSeasonalPe) return RuleOutcome.ForceInclude;
                    return r.RewardFilterAfter ? RuleOutcome.NotApplicable : RuleOutcome.Exclude;
                case SeasonalMode.UntilCsGoal:
                    // Force-assign until the user's CS goal, never below the grade floor, and never below
                    // the season's PE-CS goal (so a goal under the PE threshold cannot skip earning the PE).
                    var goal = System.Math.Max(r.EffectiveCsGoal(f.Grade), f.SeasonalPeCsGoal);
                    if((f.PreviousScoreOnThisContract ?? 0) < goal) return RuleOutcome.ForceInclude;
                    return r.RewardFilterAfter ? RuleOutcome.NotApplicable : RuleOutcome.Exclude;
                default:
                    return RuleOutcome.NotApplicable;
            }
        }
        public string Describe(RuleOutcome o) => o == RuleOutcome.Exclude
            ? "Seasonal goal met (not assigned)"
            : "Seasonal contract (force assign)";
    }
}
