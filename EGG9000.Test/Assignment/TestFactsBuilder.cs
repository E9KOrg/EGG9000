using EGG9000.Common.Contracts.Assignment;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test.Assignment {
    // Fluent builders that produce eligible-by-default AccountFacts / ContractFacts so tests vary only
    // the fact under examination. Mirrors the prod Facts/*Builder shapes but takes raw values rather
    // than a DBUser/Backup graph.
    public static class TestFactsBuilder {
        public static AccountFactsTestBuilder Account() => new();
        public static ContractFactsTestBuilder Contract() => new();
    }

    public sealed class AccountFactsTestBuilder {
        private string _accountId = "acct-1";
        private G _grade = G.GradeC;
        private bool _hasBackup = true;
        private bool _userDisabled;
        private bool _onBreak;
        private bool _hasSubscription = true;
        private double _soulEggs = 1000;
        private int _maxEggReached = 100;
        private bool _alreadyFarming;
        private bool _alreadyAssigned;
        private int _boardingGroup = 1;
        private int _completedGoals;
        private bool _previouslyCompleted;
        private bool _completedExactlyTwoGoals;
        private bool _missingColleggtible;
        private bool _missingSeasonalPe;
        private double? _previousScore;

        public AccountFactsTestBuilder AccountId(string id) { _accountId = id; return this; }
        public AccountFactsTestBuilder Grade(G grade) { _grade = grade; return this; }
        public AccountFactsTestBuilder HasBackup(bool v) { _hasBackup = v; return this; }
        public AccountFactsTestBuilder UserDisabled(bool v) { _userDisabled = v; return this; }
        public AccountFactsTestBuilder OnBreak(bool v) { _onBreak = v; return this; }
        public AccountFactsTestBuilder HasSubscription(bool v) { _hasSubscription = v; return this; }
        public AccountFactsTestBuilder SoulEggs(double v) { _soulEggs = v; return this; }
        public AccountFactsTestBuilder MaxEggReached(int v) { _maxEggReached = v; return this; }
        public AccountFactsTestBuilder BoardingGroup(int v) { _boardingGroup = v; return this; }
        public AccountFactsTestBuilder CompletedGoals(int v) { _completedGoals = v; return this; }
        public AccountFactsTestBuilder AlreadyFarming(bool v) { _alreadyFarming = v; return this; }
        public AccountFactsTestBuilder AlreadyAssigned(bool v) { _alreadyAssigned = v; return this; }
        public AccountFactsTestBuilder PreviouslyCompleted(bool v) { _previouslyCompleted = v; return this; }
        public AccountFactsTestBuilder CompletedExactlyTwoGoals(bool v) { _completedExactlyTwoGoals = v; return this; }
        public AccountFactsTestBuilder MissingColleggtible(bool v) { _missingColleggtible = v; return this; }
        public AccountFactsTestBuilder MissingSeasonalPe(bool v) { _missingSeasonalPe = v; return this; }
        public AccountFactsTestBuilder PreviousScore(double? v) { _previousScore = v; return this; }

        public AccountFacts Build() => new() {
            AccountId = _accountId,
            Grade = _grade,
            HasBackup = _hasBackup,
            UserDisabled = _userDisabled,
            OnBreak = _onBreak,
            HasActiveSubscription = _hasSubscription,
            SoulEggs = _soulEggs,
            MaxEggReached = _maxEggReached,
            AlreadyFarming = _alreadyFarming,
            AlreadyAssigned = _alreadyAssigned,
            BoardingGroup = _boardingGroup,
            CompletedGoalsOnThisContract = _completedGoals,
            PreviouslyCompleted = _previouslyCompleted,
            CompletedExactlyTwoGoals = _completedExactlyTwoGoals,
            MissingColleggtible = _missingColleggtible,
            MissingSeasonalPe = _missingSeasonalPe,
            PreviousScoreOnThisContract = _previousScore
        };
    }

    public sealed class ContractFactsTestBuilder {
        private string _contractId = "contract-1";
        private bool _isLegacy;
        private bool _isSeasonal;
        private bool _isUltra;
        private bool _isColleggtible;
        private string _colleggtibleEggId = string.Empty;
        private bool _hadTwoRewards;
        private int _egg = 1;
        private string _seasonId = string.Empty;
        private readonly Dictionary<G, GradeRewardFacts> _gradeRewards = new() {
            [G.GradeC] = new GradeRewardFacts { GoalRewards = new List<Ei.RewardType> { Ei.RewardType.Gold } }
        };

        public ContractFactsTestBuilder ContractId(string id) { _contractId = id; return this; }
        public ContractFactsTestBuilder Legacy(bool v) { _isLegacy = v; return this; }
        public ContractFactsTestBuilder Seasonal(bool v) { _isSeasonal = v; return this; }
        public ContractFactsTestBuilder Ultra(bool v) { _isUltra = v; return this; }

        public ContractFactsTestBuilder Colleggtible(bool v, string eggId = "custom") {
            _isColleggtible = v;
            _colleggtibleEggId = v ? eggId : string.Empty;
            return this;
        }

        public ContractFactsTestBuilder HadTwoRewards(bool v) { _hadTwoRewards = v; return this; }
        public ContractFactsTestBuilder Egg(int v) { _egg = v; return this; }
        public ContractFactsTestBuilder SeasonId(string id) { _seasonId = id; return this; }

        // Sets the goal rewards for a grade. Replaces the default grade map entry the first time it is
        // called (so callers fully control which grades exist).
        public ContractFactsTestBuilder Grade(G grade, params Ei.RewardType[] rewards) {
            if(!_gradeOverridden) {
                _gradeRewards.Clear();
                _gradeOverridden = true;
            }
            _gradeRewards[grade] = new GradeRewardFacts { GoalRewards = new List<Ei.RewardType>(rewards) };
            return this;
        }

        private bool _gradeOverridden;

        public ContractFacts Build() => new() {
            ContractId = _contractId,
            IsLegacy = _isLegacy,
            IsSeasonal = _isSeasonal,
            IsUltra = _isUltra,
            IsColleggtible = _isColleggtible,
            ColleggtibleEggId = _colleggtibleEggId,
            HadTwoRewards = _hadTwoRewards,
            Egg = _egg,
            SeasonId = _seasonId,
            GradeRewards = _gradeRewards
        };
    }
}
