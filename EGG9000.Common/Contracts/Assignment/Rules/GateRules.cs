namespace EGG9000.Common.Contracts.Assignment {
    public sealed class GradeUnsetRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.GradeUnset;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            f.Grade == Ei.Contract.Types.PlayerGrade.GradeUnset ? RuleOutcome.Exclude : RuleOutcome.Pass;
        public string Describe(RuleOutcome o) => "Grade is unset";
    }

    public sealed class BackupMissingRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.BackupMissing;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            !f.HasBackup ? RuleOutcome.Exclude : RuleOutcome.Pass;
        public string Describe(RuleOutcome o) => "Backup is empty";
    }

    public sealed class UserDisabledRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.UserDisabled;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            f.UserDisabled ? RuleOutcome.Exclude : RuleOutcome.Pass;
        public string Describe(RuleOutcome o) => "User disabled";
    }

    public sealed class OnBreakRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.OnBreak;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            f.OnBreak ? RuleOutcome.Exclude : RuleOutcome.Pass;
        public string Describe(RuleOutcome o) => "On break";
    }

    public sealed class NoSubscriptionRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.NoSubscription;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => c.IsUltra;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            !f.HasActiveSubscription ? RuleOutcome.Exclude : RuleOutcome.Pass;
        public string Describe(RuleOutcome o) => "Doesn't have subscription";
    }

    public sealed class InsufficientSoulEggsRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.InsufficientSoulEggs;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            f.SoulEggs < 1000 ? RuleOutcome.Exclude : RuleOutcome.Pass;
        public string Describe(RuleOutcome o) => "< 1k soul eggs";
    }

    public sealed class EggLockedRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.EggLocked;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            (f.MaxEggReached == 0 || f.MaxEggReached >= c.Egg || c.Egg >= 100) ? RuleOutcome.Pass : RuleOutcome.Exclude;
        public string Describe(RuleOutcome o) => "Egg not unlocked";
    }

    public sealed class AlreadyFarmingRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.AlreadyFarming;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            f.AlreadyFarming ? RuleOutcome.Exclude : RuleOutcome.Pass;
        public string Describe(RuleOutcome o) => "Already In Co-op";
    }

    public sealed class AlreadyAssignedRule : IAssignmentRule {
        public AssignmentRuleId Id => AssignmentRuleId.AlreadyAssigned;
        public RuleTier Tier => RuleTier.Gate;
        public bool AppliesTo(ContractFacts c) => true;
        public RuleOutcome Evaluate(AccountFacts f, ContractFacts c, AssignmentSettings s) =>
            f.AlreadyAssigned ? RuleOutcome.Exclude : RuleOutcome.Pass;
        public string Describe(RuleOutcome o) => "Already assigned a co-op";
    }
}
