namespace EGG9000.Common.Contracts.Assignment {
    public interface IAssignmentRule {
        AssignmentRuleId Id { get; }
        RuleTier Tier { get; }
        bool AppliesTo(ContractFacts contract);
        RuleOutcome Evaluate(AccountFacts facts, ContractFacts contract, AssignmentSettings settings);
        string Describe(RuleOutcome outcome);
    }
}
