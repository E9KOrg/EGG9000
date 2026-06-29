namespace EGG9000.Common.Contracts.Assignment {
    public sealed class AccountFacts {
        public required string AccountId { get; init; }
        public required Ei.Contract.Types.PlayerGrade Grade { get; init; }
        public required bool HasBackup { get; init; }
        public required bool UserDisabled { get; init; }
        public required bool OnBreak { get; init; }
        public required bool HasActiveSubscription { get; init; }
        public required double SoulEggs { get; init; }
        public required int MaxEggReached { get; init; }
        public required bool AlreadyFarming { get; init; }
        public required bool AlreadyAssigned { get; init; }
        public required int BoardingGroup { get; init; }
        public required int CompletedGoalsOnThisContract { get; init; }
        public required bool PreviouslyCompleted { get; init; }
        public required bool CompletedExactlyTwoGoals { get; init; }
        public required bool MissingColleggtible { get; init; }
        public required bool MissingSeasonalPe { get; init; }
        // CS (season Cxp) at which this account's season grade earns all its PE. 0 when the contract has
        // no season or the grade has no PE goals. Used as a floor on the UntilCsGoal seasonal goal so a
        // user goal below the PE goal cannot let them stop before earning the season PE.
        public required double SeasonalPeCsGoal { get; init; }
        public double? PreviousScoreOnThisContract { get; init; }

        // Set during evaluator pass 2 for RedoLeggacyOption.YesOtherAccountMatch.
        public bool SiblingMatchProvisionalInclude { get; set; }
    }
}
