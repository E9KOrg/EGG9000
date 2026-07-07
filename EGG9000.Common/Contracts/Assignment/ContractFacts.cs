using System.Collections.Generic;

namespace EGG9000.Common.Contracts.Assignment {
    public sealed class GradeRewardFacts {
        public required IReadOnlyList<Ei.RewardType> GoalRewards { get; init; }
    }

    public sealed class ContractFacts {
        public required string ContractId { get; init; }
        public required bool IsLegacy { get; init; }
        public required bool IsSeasonal { get; init; }
        public required bool IsUltra { get; init; }
        public required bool IsColleggtible { get; init; }
        public string ColleggtibleEggId { get; init; }
        public required bool HadTwoRewards { get; init; }
        public required int Egg { get; init; }
        public string SeasonId { get; init; }
        public required IReadOnlyDictionary<Ei.Contract.Types.PlayerGrade, GradeRewardFacts> GradeRewards { get; init; }
    }
}
