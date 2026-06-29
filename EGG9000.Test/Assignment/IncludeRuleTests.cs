using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class IncludeRuleTests {
        private static GradeRewardFacts Grade(params Ei.RewardType[] rewards) =>
            new() { GoalRewards = new List<Ei.RewardType>(rewards) };

        // RewardMatch -------------------------------------------------------

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_EmptyFilter_AlwaysMatches() {
            Assert.IsTrue(RewardMatch.Matches(Grade(Ei.RewardType.Gold), null, 0));
            Assert.IsTrue(RewardMatch.Matches(Grade(Ei.RewardType.Gold), new List<Ei.RewardType>(), 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_ArtifactAliasesArtifactCase() {
            var grade = Grade(Ei.RewardType.ArtifactCase);
            Assert.IsTrue(RewardMatch.Matches(grade, new List<Ei.RewardType> { Ei.RewardType.Artifact }, 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_PiggyMultiplierAliasesBumpAndFill() {
            var filter = new List<Ei.RewardType> { Ei.RewardType.PiggyMultiplier };
            Assert.IsTrue(RewardMatch.Matches(Grade(Ei.RewardType.PiggyLevelBump), filter, 0));
            Assert.IsTrue(RewardMatch.Matches(Grade(Ei.RewardType.PiggyFill), filter, 0));
            Assert.IsTrue(RewardMatch.Matches(Grade(Ei.RewardType.PiggyMultiplier), filter, 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_SkipsCompletedGoals() {
            // Goals: [Gold, Artifact]. Filter wants Gold. With 1 completed goal, only Artifact remains.
            var grade = Grade(Ei.RewardType.Gold, Ei.RewardType.Artifact);
            var filter = new List<Ei.RewardType> { Ei.RewardType.Gold };
            Assert.IsTrue(RewardMatch.Matches(grade, filter, 0));
            Assert.IsFalse(RewardMatch.Matches(grade, filter, 1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_NoAlias_NoMatch() {
            Assert.IsFalse(RewardMatch.Matches(Grade(Ei.RewardType.Cash), new List<Ei.RewardType> { Ei.RewardType.Gold }, 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_MatchesLast_UsesFinalGoal() {
            var grade = Grade(Ei.RewardType.Gold, Ei.RewardType.Artifact);
            Assert.IsTrue(RewardMatch.MatchesLast(grade, Ei.RewardType.Artifact));
            Assert.IsFalse(RewardMatch.MatchesLast(grade, Ei.RewardType.Gold));
            // Alias still resolves on the last goal.
            Assert.IsTrue(RewardMatch.MatchesLast(Grade(Ei.RewardType.Gold, Ei.RewardType.ArtifactCase), Ei.RewardType.Artifact));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_MatchesLast_EmptyGoals_False() {
            Assert.IsFalse(RewardMatch.MatchesLast(Grade(), Ei.RewardType.Gold));
        }

        // RewardFilterRule --------------------------------------------------

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_AppliesToAllContracts() {
            Assert.IsTrue(new RewardFilterRule().AppliesTo(TestFactsBuilder.Contract().Legacy(true).Build()));
            Assert.IsTrue(new RewardFilterRule().AppliesTo(TestFactsBuilder.Contract().Legacy(false).Build()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_NoMatch_Excludes_MatchPasses_EmptyPasses() {
            var rule = new RewardFilterRule();
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).Build();

            Assert.AreEqual(RuleOutcome.Exclude,
                rule.Evaluate(facts, contract, new AssignmentSettings { RewardFilter = new() { Ei.RewardType.Artifact } }));
            Assert.AreEqual(RuleOutcome.Pass,
                rule.Evaluate(facts, contract, new AssignmentSettings { RewardFilter = new() { Ei.RewardType.Gold } }));
            Assert.AreEqual(RuleOutcome.Pass,
                rule.Evaluate(facts, contract, new AssignmentSettings()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardFilter_MissingGradeRewards_Excludes() {
            var rule = new RewardFilterRule();
            // Contract only defines GradeC; account is GradeA -> no matching grade rewards.
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeA).Build();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(facts, contract, new AssignmentSettings()));
        }

        // PreviouslyCompletedRule -------------------------------------------

        private static AccountFactsTestBuilder Completed() =>
            TestFactsBuilder.Account().Grade(G.GradeC).PreviouslyCompleted(true);

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviouslyCompleted_NotCompleted_Passes() {
            var rule = new PreviouslyCompletedRule();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).PreviouslyCompleted(false).Build();
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build(), new AssignmentSettings()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviouslyCompleted_RedoNo_Excludes() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.No } };
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(Completed().Build(), TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build(), s));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviouslyCompleted_YesAll_Passes() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll } };
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(Completed().Build(), TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build(), s));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviouslyCompleted_YesNoUltra_DependsOnUltra() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesNoUltra } };
            var nonUltra = TestFactsBuilder.Contract().Ultra(false).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var ultra = TestFactsBuilder.Contract().Ultra(true).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(Completed().Build(), nonUltra, s));
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(Completed().Build(), ultra, s));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviouslyCompleted_YesThreshold_InclusiveBoundary() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesThreshold, ScoreThreshold = 20000 } };
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            // <= threshold -> redo allowed (Pass).
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(Completed().PreviousScore(20000).Build(), contract, s));
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(Completed().PreviousScore(19999).Build(), contract, s));
            // > threshold -> falls through to the previously-completed exclude.
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(Completed().PreviousScore(20001).Build(), contract, s));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviouslyCompleted_YesOtherAccountMatch_NeedsSiblingFlag() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesOtherAccountMatch } };
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();

            var withoutFlag = Completed().Build();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(withoutFlag, contract, s));

            var withFlag = Completed().Build();
            withFlag.SiblingMatchProvisionalInclude = true;
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(withFlag, contract, s));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviouslyCompleted_ExcludeSeasonal_OverridesRedo() {
            var rule = new PreviouslyCompletedRule();
            // YesAll would normally redo, but ExcludeSeasonal on a seasonal replay excludes.
            var s = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll, ExcludeSeasonal = true } };
            var seasonal = TestFactsBuilder.Contract().Seasonal(true).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(Completed().Build(), seasonal, s));

            // Non-seasonal with the same settings still redoes.
            var nonSeasonal = TestFactsBuilder.Contract().Seasonal(false).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(Completed().Build(), nonSeasonal, s));
        }

        // 2 -> 3 branch -----------------------------------------------------

        private static AccountFacts TwoOfThree() =>
            TestFactsBuilder.Account().Grade(G.GradeC).PreviouslyCompleted(false).CompletedExactlyTwoGoals(true).Build();

        private static ContractFacts ThreeGoalContract(Ei.RewardType third) =>
            TestFactsBuilder.Contract().HadTwoRewards(true).Grade(G.GradeC, Ei.RewardType.Gold, Ei.RewardType.Cash, third).Build();

        [TestMethod]
        [TestCategory("Unit")]
        public void TwoToThree_Disabled_Excludes() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { TwoToThree = false };
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TwoOfThree(), ThreeGoalContract(Ei.RewardType.Artifact), s));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TwoToThree_EnabledEmptyFilter_Passes() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { TwoToThree = true, RewardFilter = new() };
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(TwoOfThree(), ThreeGoalContract(Ei.RewardType.Artifact), s));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TwoToThree_EnabledThirdRewardInFilter_Passes() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { TwoToThree = true, RewardFilter = new() { Ei.RewardType.Artifact } };
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(TwoOfThree(), ThreeGoalContract(Ei.RewardType.Artifact), s));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TwoToThree_EnabledThirdRewardNotInFilter_Excludes() {
            var rule = new PreviouslyCompletedRule();
            var s = new AssignmentSettings { TwoToThree = true, RewardFilter = new() { Ei.RewardType.Cash } };
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TwoOfThree(), ThreeGoalContract(Ei.RewardType.Artifact), s));
        }
    }
}
