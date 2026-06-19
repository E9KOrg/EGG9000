namespace EGG9000.Common.Contracts.Assignment {
    public enum RuleTier { Gate, Force, Include }

    public enum AssignmentRuleId {
        GradeUnset, BackupMissing, UserDisabled, OnBreak, NoSubscription,
        InsufficientSoulEggs, EggLocked, AlreadyFarming, AlreadyAssigned,
        MissingColleggtible, MissingSeasonalPe,
        NewRewardFilter, LegacyRewardFilter, RedoCompleted, TwoToThree
    }

    // NotApplicable: rule does not apply (wrong contract type / mode NotSet / not yet resolvable).
    // ForceInclude: force rule fired - assign and skip remaining include rules.
    // Exclude: rule removes the account (decisive).
    // Pass: rule applied and did not remove the account.
    public enum RuleOutcome { NotApplicable, ForceInclude, Exclude, Pass }

    public enum PermanentRewardKind { Colleggtible, SeasonalPe }

    // No skip/Never mode. A force rule only ever adds assignment. Skipping seasonals
    // was never approved (exploit: dodging palace seasonals) and is removed.
    public enum ForceMode { NotSet, AssignIfMissing, BelowThreshold }
}
