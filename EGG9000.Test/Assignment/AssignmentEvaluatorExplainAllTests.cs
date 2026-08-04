using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class AssignmentEvaluatorExplainAllTests {
        private static AccountFacts BaseFacts(bool onBreak = false, bool missingColleggtible = false) => new() {
            AccountId = "acc1",
            Grade = Ei.Contract.Types.PlayerGrade.GradeA,
            HasBackup = true,
            UserDisabled = false,
            OnBreak = onBreak,
            HasActiveSubscription = true,
            SoulEggs = 10000,
            MaxEggReached = 50,
            AlreadyFarming = false,
            AlreadyAssigned = false,
            BoardingGroup = 1,
            CompletedGoalsOnThisContract = 0,
            PreviouslyCompleted = false,
            CompletedExactlyTwoGoals = false,
            MissingColleggtible = missingColleggtible,
            MissingSeasonalPe = false,
            SeasonalPeCsGoal = 0
        };

        private static ContractFacts BaseContract(bool isColleggtible = false) => new() {
            ContractId = "c1",
            IsLegacy = false,
            IsSeasonal = false,
            IsUltra = false,
            IsColleggtible = isColleggtible,
            HadTwoRewards = false,
            Egg = 0,
            GradeRewards = new Dictionary<Ei.Contract.Types.PlayerGrade, GradeRewardFacts> {
                [Ei.Contract.Types.PlayerGrade.GradeA] = new GradeRewardFacts { GoalRewards = [] }
            }
        };

        [TestMethod]
        public void Evaluate_GateExclude_ExplainAllRecordsLaterRulesButSameDecision() {
            var facts = BaseFacts(onBreak: true);
            var contract = BaseContract();
            var settings = new AssignmentSettings();

            var shortCircuit = AssignmentEvaluator.Evaluate(facts, contract, settings, explainAll: false);
            var full = AssignmentEvaluator.Evaluate(facts, contract, settings, explainAll: true);

            Assert.IsFalse(shortCircuit.Assigned);
            Assert.IsFalse(full.Assigned);
            Assert.IsFalse(shortCircuit.Results.Any(r => r.Rule == AssignmentRuleId.RewardFilter));
            Assert.IsTrue(full.Results.Any(r => r.Rule == AssignmentRuleId.RewardFilter));
            Assert.AreEqual(RuleOutcome.Exclude, full.Results.Single(r => r.Rule == AssignmentRuleId.OnBreak).Outcome);
        }

        [TestMethod]
        public void Evaluate_ForceIncludeThenLaterExclude_ExplainAllKeepsForceIncludeDecision() {
            var facts = BaseFacts(missingColleggtible: true);
            var contract = BaseContract(isColleggtible: true);
            var settings = new AssignmentSettings();
            settings.SetForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);
            settings.RewardFilter = [Ei.RewardType.Artifact]; // won't match empty GoalRewards -> would Exclude if it ran decisively

            var shortCircuit = AssignmentEvaluator.Evaluate(facts, contract, settings, explainAll: false);
            var full = AssignmentEvaluator.Evaluate(facts, contract, settings, explainAll: true);

            Assert.IsTrue(shortCircuit.Assigned);
            Assert.IsTrue(full.Assigned); // first decisive rule (ForceInclude) still wins even though Include later excludes
            Assert.IsFalse(shortCircuit.Results.Any(r => r.Rule == AssignmentRuleId.RewardFilter));
            Assert.IsTrue(full.Results.Any(r => r.Rule == AssignmentRuleId.RewardFilter));
            Assert.AreEqual(RuleOutcome.Exclude, full.Results.Single(r => r.Rule == AssignmentRuleId.RewardFilter).Outcome);
        }

        [TestMethod]
        public void EvaluateUser_SiblingMatch_ExplainAllThreadsIntoPass2Evaluation() {
            var contract = BaseContract();

            var factsA = BaseFacts();
            var settingsA = new AssignmentSettings();

            // Account B: RewardFilterRule excludes decisively on every evaluation (non-empty filter,
            // contract's GradeA has no goal rewards to match), so it fires before PreviouslyCompletedRule
            // in the Include tier on both pass 1 and pass 2. Redo.Mode = YesOtherAccountMatch still triggers
            // EvaluateUser's pass-2 re-evaluation regardless of pass-1's own outcome for this account.
            var settingsB = new AssignmentSettings {
                Redo = new RedoRule { Mode = RedoLeggacyOption.YesOtherAccountMatch },
                RewardFilter = [Ei.RewardType.Artifact]
            };
            var factsB = new AccountFacts {
                AccountId = "acc2",
                Grade = Ei.Contract.Types.PlayerGrade.GradeA,
                HasBackup = true,
                UserDisabled = false,
                OnBreak = false,
                HasActiveSubscription = true,
                SoulEggs = 10000,
                MaxEggReached = 50,
                AlreadyFarming = false,
                AlreadyAssigned = false,
                BoardingGroup = 1,
                CompletedGoalsOnThisContract = 0,
                PreviouslyCompleted = true,
                CompletedExactlyTwoGoals = false,
                MissingColleggtible = false,
                MissingSeasonalPe = false,
                SeasonalPeCsGoal = 0
            };

            var accounts = new List<(AccountFacts facts, AssignmentSettings settings)> {
                (factsA, settingsA),
                (factsB, settingsB)
            };

            var shortCircuit = AssignmentEvaluator.EvaluateUser(accounts, contract, verbose: true, explainAll: false);
            var full = AssignmentEvaluator.EvaluateUser(accounts, contract, verbose: true, explainAll: true);

            // Both runs: account B's pass-2 re-evaluation decisively excludes on RewardFilterRule.
            Assert.AreEqual(RuleOutcome.Exclude, shortCircuit[1].decision.Results.Single(r => r.Rule == AssignmentRuleId.RewardFilter).Outcome);
            Assert.AreEqual(RuleOutcome.Exclude, full[1].decision.Results.Single(r => r.Rule == AssignmentRuleId.RewardFilter).Outcome);

            // Only when explainAll threads into the pass-2 Evaluate call does the trace continue past
            // that decisive Exclude to record the later PreviouslyCompletedRule (RedoCompleted) entry.
            Assert.IsFalse(shortCircuit[1].decision.Results.Any(r => r.Rule == AssignmentRuleId.RedoCompleted));
            Assert.IsTrue(full[1].decision.Results.Any(r => r.Rule == AssignmentRuleId.RedoCompleted));
        }
    }
}
