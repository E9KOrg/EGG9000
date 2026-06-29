using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class EvaluatorTierPrecedenceTests {
        [TestMethod]
        [TestCategory("Unit")]
        public void EligibleNewContract_NoFilter_Assigned() {
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).Build();
            Assert.IsTrue(AssignmentEvaluator.Evaluate(facts, contract, new AssignmentSettings()).Assigned);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Gate_Beats_Force() {
            // Colleggtible force would include, but the account is on break (a gate exclude).
            var contract = TestFactsBuilder.Contract().Colleggtible(true).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).OnBreak(true).MissingColleggtible(true).Build();
            var s = new AssignmentSettings();
            s.SetForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);

            var decision = AssignmentEvaluator.Evaluate(facts, contract, s);
            Assert.IsFalse(decision.Assigned);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Force_Beats_Include() {
            // Reward filter would exclude (no match), but colleggtible force includes first.
            var contract = TestFactsBuilder.Contract().Colleggtible(true).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).MissingColleggtible(true).Build();
            var s = new AssignmentSettings { RewardFilter = new() { Ei.RewardType.Artifact } };
            s.SetForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);

            Assert.IsTrue(AssignmentEvaluator.Evaluate(facts, contract, s).Assigned);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GuildOverride_DisablesRule() {
            // Reward filter would exclude, but the guild forbids the RewardFilter rule.
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).Build();
            var s = new AssignmentSettings { RewardFilter = new() { Ei.RewardType.Artifact } };

            Assert.IsFalse(AssignmentEvaluator.Evaluate(facts, contract, s).Assigned);

            var forbidden = new HashSet<AssignmentRuleId> { AssignmentRuleId.RewardFilter };
            Assert.IsTrue(AssignmentEvaluator.Evaluate(facts, contract, s, forbidden).Assigned);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FiltersDisabled_SkipsRewardAndSeasonal_KeepsPreviouslyCompleted() {
            var contract = TestFactsBuilder.Contract().Legacy(true).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var settings = new AssignmentSettings {
                RewardFilter = new() { Ei.RewardType.Artifact },
                Redo = new RedoRule { Mode = RedoLeggacyOption.No }
            };

            // Reward filter would exclude, but it is skipped under DisableBG.
            // Previously-completed (No + completed) must STILL exclude under DisableBG.
            var completed = TestFactsBuilder.Account().Grade(G.GradeC).PreviouslyCompleted(true).Build();
            Assert.IsFalse(AssignmentEvaluator.Evaluate(completed, contract, settings, filtersDisabled: true).Assigned);

            // Not previously completed -> assigned (reward filter skipped, redo passes).
            var fresh = TestFactsBuilder.Account().Grade(G.GradeC).PreviouslyCompleted(false).Build();
            Assert.IsTrue(AssignmentEvaluator.Evaluate(fresh, contract, settings, filtersDisabled: true).Assigned);
        }
    }
}
