using EGG9000.Common.Contracts.Assignment;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Common.Test.Assignment {
    public static class TestFactsBuilder {
        public static AccountFactsBuilder Account() => new();
        public static ContractFactsBuilder Contract() => new();
    }

    public sealed class AccountFactsBuilder {
        private G _grade = G.GradeC;
        private bool _hasBackup = true, _disabled, _onBreak, _hasSub = true;
        private double _soulEggs = 1000;
        private int _maxEgg = 100, _completedGoals, _group = 1;
        private bool _farming, _assigned, _prevComplete, _twoGoals, _missingColl, _missingPe;
        private double? _prevScore;

        public AccountFactsBuilder Grade(G g) { _grade = g; return this; }
        public AccountFactsBuilder HasBackup(bool v) { _hasBackup = v; return this; }
        public AccountFactsBuilder UserDisabled(bool v) { _disabled = v; return this; }
        public AccountFactsBuilder OnBreak(bool v) { _onBreak = v; return this; }
        public AccountFactsBuilder HasSubscription(bool v) { _hasSub = v; return this; }
        public AccountFactsBuilder SoulEggs(double v) { _soulEggs = v; return this; }
        public AccountFactsBuilder MaxEggReached(int v) { _maxEgg = v; return this; }
        public AccountFactsBuilder BoardingGroup(int v) { _group = v; return this; }
        public AccountFactsBuilder CompletedGoals(int v) { _completedGoals = v; return this; }
        public AccountFactsBuilder AlreadyFarming(bool v) { _farming = v; return this; }
        public AccountFactsBuilder AlreadyAssigned(bool v) { _assigned = v; return this; }
        public AccountFactsBuilder PreviouslyCompleted(bool v) { _prevComplete = v; return this; }
        public AccountFactsBuilder CompletedExactlyTwoGoals(bool v) { _twoGoals = v; return this; }
        public AccountFactsBuilder MissingColleggtible(bool v) { _missingColl = v; return this; }
        public AccountFactsBuilder MissingSeasonalPe(bool v) { _missingPe = v; return this; }
        public AccountFactsBuilder PreviousScore(double? v) { _prevScore = v; return this; }

        public AccountFacts Build() => new() {
            AccountId = "acct", Grade = _grade, HasBackup = _hasBackup, UserDisabled = _disabled,
            OnBreak = _onBreak, HasActiveSubscription = _hasSub, SoulEggs = _soulEggs,
            MaxEggReached = _maxEgg, AlreadyFarming = _farming, AlreadyAssigned = _assigned,
            BoardingGroup = _group, CompletedGoalsOnThisContract = _completedGoals,
            PreviouslyCompleted = _prevComplete, CompletedExactlyTwoGoals = _twoGoals,
            MissingColleggtible = _missingColl, MissingSeasonalPe = _missingPe,
            PreviousScoreOnThisContract = _prevScore
        };
    }

    public sealed class ContractFactsBuilder {
        private bool _legacy, _seasonal, _ultra, _coll, _twoRewards;
        private string? _collEgg;
        private string? _seasonId;
        private int _egg = 1;
        private readonly Dictionary<G, GradeRewardFacts> _grades = new() {
            { G.GradeC, new GradeRewardFacts { GoalRewards = new List<Ei.RewardType> { Ei.RewardType.Gold } } }
        };

        public ContractFactsBuilder Legacy(bool v) { _legacy = v; return this; }
        public ContractFactsBuilder Seasonal(bool v) { _seasonal = v; if(v && _seasonId is null) _seasonId = "s1"; return this; }
        public ContractFactsBuilder Ultra(bool v) { _ultra = v; return this; }
        public ContractFactsBuilder Colleggtible(bool v, string eggId = "egg") { _coll = v; _collEgg = eggId; return this; }
        public ContractFactsBuilder HadTwoRewards(bool v) { _twoRewards = v; return this; }
        public ContractFactsBuilder Egg(int v) { _egg = v; return this; }
        public ContractFactsBuilder SeasonId(string v) { _seasonId = v; _seasonal = v != null; return this; }
        public ContractFactsBuilder Grade(G g, params Ei.RewardType[] goals) {
            _grades[g] = new GradeRewardFacts { GoalRewards = goals };
            return this;
        }

        public ContractFacts Build() => new() {
            ContractId = "ctr", IsLegacy = _legacy, IsSeasonal = _seasonal, IsUltra = _ultra,
            IsColleggtible = _coll, ColleggtibleEggId = _collEgg, HadTwoRewards = _twoRewards,
            Egg = _egg, SeasonId = _seasonId, GradeRewards = _grades
        };
    }
}
