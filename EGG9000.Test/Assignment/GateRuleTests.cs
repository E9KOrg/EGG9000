using EGG9000.Common.Contracts.Assignment;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class GateRuleTests {
        private static readonly AssignmentSettings S = new();

        private static RuleOutcome Eval(IAssignmentRule rule, AccountFactsTestBuilder acct, ContractFactsTestBuilder contract) =>
            rule.Evaluate(acct.Build(), contract.Build(), S);

        [TestMethod]
        [TestCategory("Unit")]
        public void GradeUnset_ExcludesOnlyWhenUnset() {
            var rule = new GradeUnsetRule();
            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().Grade(G.GradeUnset), TestFactsBuilder.Contract()));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().Grade(G.GradeC), TestFactsBuilder.Contract()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void BackupMissing_ExcludesWhenNoBackup() {
            var rule = new BackupMissingRule();
            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().HasBackup(false), TestFactsBuilder.Contract()));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().HasBackup(true), TestFactsBuilder.Contract()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void UserDisabled_ExcludesWhenDisabled() {
            var rule = new UserDisabledRule();
            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().UserDisabled(true), TestFactsBuilder.Contract()));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().UserDisabled(false), TestFactsBuilder.Contract()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void OnBreak_ExcludesWhenOnBreak() {
            var rule = new OnBreakRule();
            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().OnBreak(true), TestFactsBuilder.Contract()));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().OnBreak(false), TestFactsBuilder.Contract()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NoSubscription_AppliesOnlyToUltra() {
            var rule = new NoSubscriptionRule();
            Assert.IsFalse(rule.AppliesTo(TestFactsBuilder.Contract().Ultra(false).Build()));
            Assert.IsTrue(rule.AppliesTo(TestFactsBuilder.Contract().Ultra(true).Build()));

            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().HasSubscription(false), TestFactsBuilder.Contract().Ultra(true)));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().HasSubscription(true), TestFactsBuilder.Contract().Ultra(true)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void InsufficientSoulEggs_BoundaryAt1000() {
            var rule = new InsufficientSoulEggsRule();
            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().SoulEggs(999), TestFactsBuilder.Contract()));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().SoulEggs(1000), TestFactsBuilder.Contract()));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().SoulEggs(1001), TestFactsBuilder.Contract()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void EggLocked_PassWhenReachedOrZeroOrEggAtLeast100() {
            var rule = new EggLockedRule();
            // MaxEggReached below contract egg -> locked.
            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().MaxEggReached(3), TestFactsBuilder.Contract().Egg(5)));
            // Reached exactly -> pass.
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().MaxEggReached(5), TestFactsBuilder.Contract().Egg(5)));
            // Reached above -> pass.
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().MaxEggReached(9), TestFactsBuilder.Contract().Egg(5)));
            // MaxEggReached == 0 (no backup data) -> allowed.
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().MaxEggReached(0), TestFactsBuilder.Contract().Egg(5)));
            // Egg >= 100 (custom/colleggtible eggs) -> always allowed even if not reached.
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().MaxEggReached(3), TestFactsBuilder.Contract().Egg(100)));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlreadyFarming_Excludes() {
            var rule = new AlreadyFarmingRule();
            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().AlreadyFarming(true), TestFactsBuilder.Contract()));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().AlreadyFarming(false), TestFactsBuilder.Contract()));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlreadyAssigned_Excludes() {
            var rule = new AlreadyAssignedRule();
            Assert.AreEqual(RuleOutcome.Exclude, Eval(rule, TestFactsBuilder.Account().AlreadyAssigned(true), TestFactsBuilder.Contract()));
            Assert.AreEqual(RuleOutcome.Pass, Eval(rule, TestFactsBuilder.Account().AlreadyAssigned(false), TestFactsBuilder.Contract()));
        }
    }
}
