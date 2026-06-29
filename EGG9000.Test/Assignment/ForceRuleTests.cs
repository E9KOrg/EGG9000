using EGG9000.Common.Contracts.Assignment;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class ForceRuleTests {
        private static AssignmentSettings WithColleggtible(ForceMode mode) {
            var s = new AssignmentSettings();
            s.SetForce(PermanentRewardKind.Colleggtible, mode);
            return s;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_AppliesOnlyToColleggtibleContracts() {
            var rule = new ColleggtibleForceRule();
            Assert.IsFalse(rule.AppliesTo(TestFactsBuilder.Contract().Colleggtible(false).Build()));
            Assert.IsTrue(rule.AppliesTo(TestFactsBuilder.Contract().Colleggtible(true).Build()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_MissingAndEnabled_ForceIncludes() {
            var rule = new ColleggtibleForceRule();
            var contract = TestFactsBuilder.Contract().Colleggtible(true).Build();
            var facts = TestFactsBuilder.Account().MissingColleggtible(true).Build();
            Assert.AreEqual(RuleOutcome.ForceInclude, rule.Evaluate(facts, contract, WithColleggtible(ForceMode.AssignIfMissing)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_NotMissing_NotApplicable() {
            var rule = new ColleggtibleForceRule();
            var contract = TestFactsBuilder.Contract().Colleggtible(true).Build();
            var facts = TestFactsBuilder.Account().MissingColleggtible(false).Build();
            Assert.AreEqual(RuleOutcome.NotApplicable, rule.Evaluate(facts, contract, WithColleggtible(ForceMode.AssignIfMissing)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_Disabled_NotApplicable() {
            var rule = new ColleggtibleForceRule();
            var contract = TestFactsBuilder.Contract().Colleggtible(true).Build();
            var facts = TestFactsBuilder.Account().MissingColleggtible(true).Build();
            Assert.AreEqual(RuleOutcome.NotApplicable, rule.Evaluate(facts, contract, WithColleggtible(ForceMode.NotSet)));
        }
    }
}
