using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Contracts.Assignment.Diagnostics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class AssignmentParityTests {
        // v2 parity classifier: a mismatch is a by-design seasonal deviation when the new engine's
        // decision was driven by the SeasonalContractsRule short-circuiting with ForceInclude (mandatory
        // seasonal assignment) or Exclude (seasonal goal met). The old logic had no mandatory-seasonal
        // tier, so a decisive seasonal rule means the divergence is the intended redesign, not a
        // regression. Anything else stays flagged unexpected.

        private static AssignmentDecision DecisionWith(bool assigned, params RuleResult[] results) =>
            new() { Assigned = assigned, Results = results };

        private static RuleResult Seasonal(RuleOutcome outcome) =>
            new(AssignmentRuleId.SeasonalContracts, RuleTier.Force, outcome, "seasonal");

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalForceInclude_IsExpected() {
            var decision = DecisionWith(true, Seasonal(RuleOutcome.ForceInclude));
            Assert.IsTrue(AssignmentParityChecker.IsSeasonalDecisive(decision));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalExclude_IsExpected() {
            var decision = DecisionWith(false, Seasonal(RuleOutcome.Exclude));
            Assert.IsTrue(AssignmentParityChecker.IsSeasonalDecisive(decision));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalNotApplicable_IsNotExpected() {
            var decision = DecisionWith(true, Seasonal(RuleOutcome.NotApplicable));
            Assert.IsFalse(AssignmentParityChecker.IsSeasonalDecisive(decision));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NonSeasonalDecision_IsNotExpected() {
            var decision = DecisionWith(false,
                new RuleResult(AssignmentRuleId.RewardFilter, RuleTier.Include, RuleOutcome.Exclude, "rewards"));
            Assert.IsFalse(AssignmentParityChecker.IsSeasonalDecisive(decision));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EmptyResults_IsNotExpected() {
            var decision = DecisionWith(true);
            Assert.IsFalse(AssignmentParityChecker.IsSeasonalDecisive(decision));
        }
    }
}
