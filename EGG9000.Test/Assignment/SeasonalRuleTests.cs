using EGG9000.Common.Contracts.Assignment;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class SeasonalRuleTests {
        private static AssignmentSettings With(SeasonalMode mode, double goal = 0, bool after = false) =>
            new() { Seasonal = new SeasonalRule { Mode = mode, CsGoal = goal, RewardFilterAfter = after } };

        [TestMethod]
        [TestCategory("Unit")]
        public void OnlyAppliesToSeasonal() {
            Assert.IsFalse(new SeasonalContractsRule().AppliesTo(TestFactsBuilder.Contract().Seasonal(false).Build()));
            Assert.IsTrue(new SeasonalContractsRule().AppliesTo(TestFactsBuilder.Contract().Seasonal(true).Build()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlwaysAssign_Forces() {
            var c = TestFactsBuilder.Contract().Seasonal(true).Build();
            var f = TestFactsBuilder.Account().MissingSeasonalPe(false).Build();
            Assert.AreEqual(RuleOutcome.ForceInclude, new SeasonalContractsRule().Evaluate(f, c, With(SeasonalMode.AlwaysAssign)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UntilPe_MissingForces_EarnedStopsOrFilters() {
            var rule = new SeasonalContractsRule();
            var c = TestFactsBuilder.Contract().Seasonal(true).Build();

            Assert.AreEqual(RuleOutcome.ForceInclude,
                rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(true).Build(), c, With(SeasonalMode.UntilPeEarned)));
            // earned + after=false -> stop (Exclude)
            Assert.AreEqual(RuleOutcome.Exclude,
                rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(false).Build(), c, With(SeasonalMode.UntilPeEarned, after: false)));
            // earned + after=true -> fall through (NotApplicable)
            Assert.AreEqual(RuleOutcome.NotApplicable,
                rule.Evaluate(TestFactsBuilder.Account().MissingSeasonalPe(false).Build(), c, With(SeasonalMode.UntilPeEarned, after: true)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UntilCsGoal_BelowForces_AtOrAboveStopsOrFilters() {
            var rule = new SeasonalContractsRule();
            var c = TestFactsBuilder.Contract().Seasonal(true).Build();

            Assert.AreEqual(RuleOutcome.ForceInclude,
                rule.Evaluate(TestFactsBuilder.Account().PreviousScore(4000).Build(), c, With(SeasonalMode.UntilCsGoal, goal: 5000)));
            // at goal + after=false -> stop (Exclude); 5000 is not < 5000.
            Assert.AreEqual(RuleOutcome.Exclude,
                rule.Evaluate(TestFactsBuilder.Account().PreviousScore(5000).Build(), c, With(SeasonalMode.UntilCsGoal, goal: 5000, after: false)));
            // above goal + after=false -> stop (Exclude)
            Assert.AreEqual(RuleOutcome.Exclude,
                rule.Evaluate(TestFactsBuilder.Account().PreviousScore(6000).Build(), c, With(SeasonalMode.UntilCsGoal, goal: 5000, after: false)));
            // above goal + after=true -> fall through (NotApplicable)
            Assert.AreEqual(RuleOutcome.NotApplicable,
                rule.Evaluate(TestFactsBuilder.Account().PreviousScore(6000).Build(), c, With(SeasonalMode.UntilCsGoal, goal: 5000, after: true)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UntilCsGoal_NullScoreTreatedAsZero_Forces() {
            var rule = new SeasonalContractsRule();
            var c = TestFactsBuilder.Contract().Seasonal(true).Build();
            Assert.AreEqual(RuleOutcome.ForceInclude,
                rule.Evaluate(TestFactsBuilder.Account().PreviousScore(null).Build(), c, With(SeasonalMode.UntilCsGoal, goal: 5000)));
        }
    }
}
