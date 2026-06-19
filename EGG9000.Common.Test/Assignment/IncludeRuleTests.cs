using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class IncludeRuleTests {
        private static GradeRewardFacts Grade(params Ei.RewardType[] goals) => new() { GoalRewards = goals };

        // ---- RewardMatch ----

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_EmptyFilter_IsTrue() {
            Assert.IsTrue(RewardMatch.Matches(Grade(Ei.RewardType.Gold), new List<Ei.RewardType>(), 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_ArtifactAlias_MatchesArtifactCase() {
            Assert.IsTrue(RewardMatch.Matches(Grade(Ei.RewardType.ArtifactCase), new List<Ei.RewardType> { Ei.RewardType.Artifact }, 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_PiggyAlias_MatchesFill() {
            Assert.IsTrue(RewardMatch.Matches(Grade(Ei.RewardType.PiggyFill), new List<Ei.RewardType> { Ei.RewardType.PiggyMultiplier }, 0));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_SkipsCompletedGoals() {
            Assert.IsFalse(RewardMatch.Matches(Grade(Ei.RewardType.Gold, Ei.RewardType.Boost), new List<Ei.RewardType> { Ei.RewardType.Gold }, 1));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void RewardMatch_Last_UsesFinalGoal() {
            Assert.IsTrue(RewardMatch.MatchesLast(Grade(Ei.RewardType.Gold, Ei.RewardType.Artifact), Ei.RewardType.Artifact));
            Assert.IsFalse(RewardMatch.MatchesLast(Grade(Ei.RewardType.Artifact, Ei.RewardType.Gold), Ei.RewardType.Artifact));
        }

        // ---- New reward filter ----

        [TestMethod]
        [TestCategory("Unit")]
        public void NewRewardFilter_DormantOnLegacy() {
            Assert.IsFalse(new NewRewardFilterRule().AppliesTo(TestFactsBuilder.Contract().Legacy(true).Build()));
            Assert.IsTrue(new NewRewardFilterRule().AppliesTo(TestFactsBuilder.Contract().Legacy(false).Build()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NewRewardFilter_NoMatch_Excludes_MatchPasses_EmptyPasses() {
            var rule = new NewRewardFilterRule();
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).Build();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(facts, contract, new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Artifact } }));
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, contract, new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Gold } }));
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, contract, new AssignmentSettings()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NewRewardFilter_MissingGrade_Excludes() {
            var rule = new NewRewardFilterRule();
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeAaa).Build();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(facts, contract, new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Gold } }));
        }

        // ---- Legacy reward filter ----

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyRewardFilter_FallsBackToNewWhenLegacyEmpty() {
            var rule = new LegacyRewardFilterRule();
            var contract = TestFactsBuilder.Contract().Legacy(true).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).Build();
            // Legacy empty, new filter requires Gold -> match.
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, contract, new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Gold } }));
            // Legacy empty, new filter requires Artifact -> no match.
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(facts, contract, new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Artifact } }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void LegacyRewardFilter_UsesLegacyListWhenSet_PeRetained() {
            var rule = new LegacyRewardFilterRule();
            var contract = TestFactsBuilder.Contract().Legacy(true).Grade(G.GradeC, Ei.RewardType.EggsOfProphecy).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).Build();
            var settings = new AssignmentSettings {
                LegacyRewardFilter = new() { Ei.RewardType.EggsOfProphecy },
                NewContractRewardFilter = new() { Ei.RewardType.Artifact }
            };
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, contract, settings));
        }

        // ---- Previously completed / redo / 2->3 ----

        [TestMethod]
        [TestCategory("Unit")]
        public void PreviouslyCompleted_NotCompleted_Passes() {
            var rule = new PreviouslyCompletedRule();
            var facts = TestFactsBuilder.Account().PreviouslyCompleted(false).Build();
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, TestFactsBuilder.Contract().Legacy(true).Build(), new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.No } }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Redo_No_PreviouslyCompleted_Excludes() {
            var rule = new PreviouslyCompletedRule();
            var facts = TestFactsBuilder.Account().PreviouslyCompleted(true).Build();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(facts, TestFactsBuilder.Contract().Legacy(true).Build(), new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.No } }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Redo_YesAll_Passes() {
            var rule = new PreviouslyCompletedRule();
            var facts = TestFactsBuilder.Account().PreviouslyCompleted(true).Build();
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, TestFactsBuilder.Contract().Legacy(true).Build(), new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll } }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Redo_YesNoUltra_ExcludesUltra_PassesStandard() {
            var rule = new PreviouslyCompletedRule();
            var facts = TestFactsBuilder.Account().PreviouslyCompleted(true).Build();
            var settings = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesNoUltra } };
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, TestFactsBuilder.Contract().Legacy(true).Ultra(false).Build(), settings));
            // Ultra -> not passed by YesNoUltra, falls to default -> previously completed -> Exclude.
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(facts, TestFactsBuilder.Contract().Legacy(true).Ultra(true).Build(), settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Redo_YesThreshold_BoundaryInclusive() {
            var rule = new PreviouslyCompletedRule();
            var contract = TestFactsBuilder.Contract().Legacy(true).Build();
            var settings = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesThreshold, ScoreThreshold = 20000 } };
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(TestFactsBuilder.Account().PreviouslyCompleted(true).PreviousScore(20000).Build(), contract, settings));
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().PreviouslyCompleted(true).PreviousScore(20001).Build(), contract, settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Redo_YesOtherAccountMatch_RequiresSiblingFlag() {
            var rule = new PreviouslyCompletedRule();
            var contract = TestFactsBuilder.Contract().Legacy(true).Build();
            var settings = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesOtherAccountMatch } };
            var without = TestFactsBuilder.Account().PreviouslyCompleted(true).Build();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(without, contract, settings));
            var with = TestFactsBuilder.Account().PreviouslyCompleted(true).Build();
            with.SiblingMatchProvisionalInclude = true;
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(with, contract, settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void ExcludeSeasonal_OverridesRedoMode() {
            var rule = new PreviouslyCompletedRule();
            var contract = TestFactsBuilder.Contract().Legacy(true).Seasonal(true).Build();
            var settings = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll, ExcludeSeasonal = true } };
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().PreviouslyCompleted(true).Build(), contract, settings));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TwoToThree_Disabled_Excludes() {
            var rule = new PreviouslyCompletedRule();
            var contract = TestFactsBuilder.Contract().Legacy(true).HadTwoRewards(true).Grade(G.GradeC, Ei.RewardType.Gold, Ei.RewardType.Boost, Ei.RewardType.Artifact).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).CompletedExactlyTwoGoals(true).Build();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(facts, contract, new AssignmentSettings { TwoToThree = false, Redo = new RedoRule { Mode = RedoLeggacyOption.No } }));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void TwoToThree_Enabled_ThirdRewardMatch_Passes_NoMatch_Excludes() {
            var rule = new PreviouslyCompletedRule();
            var contract = TestFactsBuilder.Contract().Legacy(true).HadTwoRewards(true).Grade(G.GradeC, Ei.RewardType.Gold, Ei.RewardType.Boost, Ei.RewardType.Artifact).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).CompletedExactlyTwoGoals(true).Build();
            // Filter wants Artifact (the 3rd reward) -> pass.
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, contract, new AssignmentSettings { TwoToThree = true, LegacyRewardFilter = new() { Ei.RewardType.Artifact }, Redo = new RedoRule { Mode = RedoLeggacyOption.No } }));
            // Filter wants Gold (not the 3rd) -> exclude.
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(facts, contract, new AssignmentSettings { TwoToThree = true, LegacyRewardFilter = new() { Ei.RewardType.Gold }, Redo = new RedoRule { Mode = RedoLeggacyOption.No } }));
            // Empty filter -> pass.
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(facts, contract, new AssignmentSettings { TwoToThree = true, Redo = new RedoRule { Mode = RedoLeggacyOption.No } }));
        }
    }
}
