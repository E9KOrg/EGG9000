using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class EvaluatorTierPrecedenceTests {
        private static AssignmentSettings Colleggtible() =>
            new() { ForceRules = new List<PermanentRewardRule> { new() { Kind = PermanentRewardKind.Colleggtible, Mode = ForceMode.AssignIfMissing } } };

        [TestMethod]
        [TestCategory("Unit")]
        public void Gate_BeatsForce() {
            var contract = TestFactsBuilder.Contract().Colleggtible(true).Build();
            var facts = TestFactsBuilder.Account().OnBreak(true).MissingColleggtible(true).Build();
            var d = AssignmentEvaluator.Evaluate(facts, contract, Colleggtible());
            Assert.IsFalse(d.Assigned);
            Assert.AreEqual("On break", d.ExclusionReason);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void Force_BeatsIncludeFilter() {
            var contract = TestFactsBuilder.Contract().Colleggtible(true).Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().MissingColleggtible(true).Build();
            var settings = Colleggtible();
            settings.NewContractRewardFilter = new() { Ei.RewardType.Artifact };
            Assert.IsTrue(AssignmentEvaluator.Evaluate(facts, contract, settings).Assigned);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GuildOverride_DisablesRule() {
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            var facts = TestFactsBuilder.Account().Grade(G.GradeC).Build();
            var settings = new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Artifact } };
            Assert.IsFalse(AssignmentEvaluator.Evaluate(facts, contract, settings).Assigned);
            var forbidden = new HashSet<AssignmentRuleId> { AssignmentRuleId.NewRewardFilter };
            Assert.IsTrue(AssignmentEvaluator.Evaluate(facts, contract, settings, forbidden).Assigned);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void FiltersDisabled_SkipsForceAndInclude_GatesStillApply() {
            var contract = TestFactsBuilder.Contract().Build();
            var settings = new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Artifact } };
            Assert.IsFalse(AssignmentEvaluator.Evaluate(TestFactsBuilder.Account().OnBreak(true).Build(), contract, settings, filtersDisabled: true).Assigned);
            Assert.IsTrue(AssignmentEvaluator.Evaluate(TestFactsBuilder.Account().Build(), contract, settings, filtersDisabled: true).Assigned);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EligibleNewContract_NoFilter_Assigned() {
            Assert.IsTrue(AssignmentEvaluator.Evaluate(TestFactsBuilder.Account().Build(), TestFactsBuilder.Contract().Build(), new AssignmentSettings()).Assigned);
        }
    }
}
