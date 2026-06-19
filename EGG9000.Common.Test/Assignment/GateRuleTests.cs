using EGG9000.Common.Contracts.Assignment;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class GateRuleTests {
        private static readonly AssignmentSettings Empty = new();

        [TestMethod]
        [TestCategory("Unit")]
        public void GradeUnset_Excludes() {
            var rule = new GradeUnsetRule();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().Grade(G.GradeUnset).Build(), TestFactsBuilder.Contract().Build(), Empty));
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(TestFactsBuilder.Account().Grade(G.GradeC).Build(), TestFactsBuilder.Contract().Build(), Empty));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackupMissing_Excludes() {
            var rule = new BackupMissingRule();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().HasBackup(false).Build(), TestFactsBuilder.Contract().Build(), Empty));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UserDisabled_Excludes() {
            var rule = new UserDisabledRule();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().UserDisabled(true).Build(), TestFactsBuilder.Contract().Build(), Empty));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OnBreak_Excludes() {
            var rule = new OnBreakRule();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().OnBreak(true).Build(), TestFactsBuilder.Contract().Build(), Empty));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NoSubscription_OnlyAppliesToUltra_AndExcludes() {
            var rule = new NoSubscriptionRule();
            Assert.IsFalse(rule.AppliesTo(TestFactsBuilder.Contract().Ultra(false).Build()));
            Assert.IsTrue(rule.AppliesTo(TestFactsBuilder.Contract().Ultra(true).Build()));
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().HasSubscription(false).Build(), TestFactsBuilder.Contract().Ultra(true).Build(), Empty));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void SoulEggs_BoundaryAt1000_Passes() {
            var rule = new InsufficientSoulEggsRule();
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(TestFactsBuilder.Account().SoulEggs(1000).Build(), TestFactsBuilder.Contract().Build(), Empty));
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().SoulEggs(999).Build(), TestFactsBuilder.Contract().Build(), Empty));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EggLocked_Rules() {
            var rule = new EggLockedRule();
            // Egg 100+ always allowed regardless of progress.
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(TestFactsBuilder.Account().MaxEggReached(1).Build(), TestFactsBuilder.Contract().Egg(100).Build(), Empty));
            // MaxEggReached 0 always allowed.
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(TestFactsBuilder.Account().MaxEggReached(0).Build(), TestFactsBuilder.Contract().Egg(5).Build(), Empty));
            // Locked: contract egg above reached.
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().MaxEggReached(3).Build(), TestFactsBuilder.Contract().Egg(5).Build(), Empty));
            // Unlocked: reached >= contract egg.
            Assert.AreEqual(RuleOutcome.Pass, rule.Evaluate(TestFactsBuilder.Account().MaxEggReached(5).Build(), TestFactsBuilder.Contract().Egg(5).Build(), Empty));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlreadyFarming_Excludes() {
            var rule = new AlreadyFarmingRule();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().AlreadyFarming(true).Build(), TestFactsBuilder.Contract().Build(), Empty));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlreadyAssigned_Excludes() {
            var rule = new AlreadyAssignedRule();
            Assert.AreEqual(RuleOutcome.Exclude, rule.Evaluate(TestFactsBuilder.Account().AlreadyAssigned(true).Build(), TestFactsBuilder.Contract().Build(), Empty));
        }
    }
}
