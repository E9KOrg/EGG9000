using EGG9000.Common.Contracts.Assignment;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class ForceRuleTests {
        private static AssignmentSettings WithForce(PermanentRewardKind kind, ForceMode mode, double? floor = null) =>
            new() { ForceRules = new List<PermanentRewardRule> { new() { Kind = kind, Mode = mode, CsFloor = floor } } };

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_OnlyAppliesToColleggtibleContracts() {
            var rule = new ColleggtibleForceRule();
            Assert.IsFalse(rule.AppliesTo(TestFactsBuilder.Contract().Colleggtible(false).Build()));
            Assert.IsTrue(rule.AppliesTo(TestFactsBuilder.Contract().Colleggtible(true).Build()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Colleggtible_MissingAndEnabled_Forces() {
            var rule = new ColleggtibleForceRule();
            var contract = TestFactsBuilder.Contract().Colleggtible(true).Build();
            var settings = WithForce(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing);
            Assert.AreEqual(RuleOutcome.ForceInclude, rule.Evaluate(TestFactsBuilder.Account().MissingColleggtible(true).Build(), contract, settings));
            // Not missing -> falls through.
            Assert.AreEqual(RuleOutcome.NotApplicable, rule.Evaluate(TestFactsBuilder.Account().MissingColleggtible(false).Build(), contract, settings));
            // Disabled -> falls through even if missing.
            Assert.AreEqual(RuleOutcome.NotApplicable, rule.Evaluate(TestFactsBuilder.Account().MissingColleggtible(true).Build(), contract, new AssignmentSettings()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalPe_OnlyAppliesToSeasonal() {
            var rule = new SeasonalPeForceRule();
            Assert.IsFalse(rule.AppliesTo(TestFactsBuilder.Contract().Seasonal(false).Build()));
            Assert.IsTrue(rule.AppliesTo(TestFactsBuilder.Contract().Seasonal(true).Build()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalPe_AssignIfMissing_ForcesOnlyWhenMissing() {
            var rule = new SeasonalPeForceRule();
            var contract = TestFactsBuilder.Contract().Seasonal(true).Build();
            var settings = WithForce(PermanentRewardKind.SeasonalPe, ForceMode.AssignIfMissing);
            Assert.AreEqual(RuleOutcome.ForceInclude, rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(true).Build(), contract, settings));
            // Already have it -> never excludes, falls through to normal filters.
            Assert.AreEqual(RuleOutcome.NotApplicable, rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(false).Build(), contract, settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalPe_BelowThreshold_ForcesOnlyUnderFloor() {
            var rule = new SeasonalPeForceRule();
            var contract = TestFactsBuilder.Contract().Seasonal(true).Build();
            var settings = WithForce(PermanentRewardKind.SeasonalPe, ForceMode.BelowThreshold, 5000);
            Assert.AreEqual(RuleOutcome.ForceInclude, rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(true).PreviousScore(4000).Build(), contract, settings));
            Assert.AreEqual(RuleOutcome.NotApplicable, rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(true).PreviousScore(6000).Build(), contract, settings));
            // Below floor but not missing -> still falls through.
            Assert.AreEqual(RuleOutcome.NotApplicable, rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(false).PreviousScore(0).Build(), contract, settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SeasonalPe_NeverExcludes() {
            // No mode produces Exclude - the rule can only force-include or fall through.
            var rule = new SeasonalPeForceRule();
            var contract = TestFactsBuilder.Contract().Seasonal(true).Build();
            foreach(var mode in new[] { ForceMode.NotSet, ForceMode.AssignIfMissing, ForceMode.BelowThreshold }) {
                var settings = WithForce(PermanentRewardKind.SeasonalPe, mode, 1000);
                var outcome = rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(false).PreviousScore(999999).Build(), contract, settings);
                Assert.AreNotEqual(RuleOutcome.Exclude, outcome, $"mode {mode}");
            }
        }
    }
}
