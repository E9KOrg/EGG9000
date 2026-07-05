namespace EGG9000.Common.Contracts.Assignment {
    public enum RuleTier { Gate, Force, Include }

    public enum AssignmentRuleId {
        GradeUnset, BackupMissing, UserDisabled, OnBreak, NoSubscription,
        InsufficientSoulEggs, EggLocked, AlreadyFarming, AlreadyAssigned,
        MissingColleggtible, SeasonalContracts,
        RewardFilter, RedoCompleted, TwoToThree
    }

    // Seasonal Contracts assignment mode (mandatory; no off). Extensible.
    public enum SeasonalMode { AlwaysAssign, UntilPeEarned, UntilCsGoal }

    // NotApplicable: rule does not apply (wrong contract type / mode NotSet / not yet resolvable).
    // ForceInclude: force rule fired - assign and skip remaining include rules.
    // Exclude: rule removes the account (decisive).
    // Pass: rule applied and did not remove the account.
    public enum RuleOutcome { NotApplicable, ForceInclude, Exclude, Pass }

    // SeasonalPe is vestigial (seasonal moved to SeasonalRule in v2). Kept so any old ForceRules entry
    // from a v1 blob still deserializes to a named value; ColleggtibleForceRule ignores it.
    public enum PermanentRewardKind { Colleggtible, SeasonalPe }

    // No skip/Never mode. A force rule only ever adds assignment. Skipping seasonals
    // was never approved (exploit: dodging palace seasonals) and is removed.
    public enum ForceMode { NotSet, AssignIfMissing, BelowThreshold }
}
