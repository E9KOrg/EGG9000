using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ContractGetGoalsTests {
        private const double BaseTarget = 10;
        private const double GoalSetBase = 100;
        private const double GradeSpecBase = 1000;

        private static Ei.Contract Build(int gradeSpecs, int goalSets, bool baseGoals) {
            var contract = new Ei.Contract { Identifier = "test" };
            if(baseGoals)
                contract.Goals.Add(new Ei.Contract.Types.Goal { TargetAmount = BaseTarget });
            for(var i = 0; i < goalSets; i++) {
                var set = new Ei.Contract.Types.GoalSet();
                set.Goals.Add(new Ei.Contract.Types.Goal { TargetAmount = GoalSetBase + i });
                contract.GoalSets.Add(set);
            }
            for(var i = 0; i < gradeSpecs; i++) {
                var spec = new Ei.Contract.Types.GradeSpec { Grade = (Ei.Contract.Types.PlayerGrade)(i + 1) };
                spec.Goals.Add(new Ei.Contract.Types.Goal { TargetAmount = GradeSpecBase + i });
                contract.GradeSpecs.Add(spec);
            }
            return contract;
        }

        private static double Single(System.Collections.Generic.List<Ei.Contract.Types.Goal> goals) {
            Assert.HasCount(1, goals);
            return goals.Single().TargetAmount;
        }

        [TestMethod]
        public void LeagueZero_WithGradeSpecsAndGoalSets_FallsBackToFirstGoalSet() {
            var contract = Build(5, 2, true);

            Assert.AreEqual(GoalSetBase, Single(contract.GetGoals(0)));
        }

        [TestMethod]
        public void LeagueZero_WithGradeSpecsOnly_FallsBackToBaseGoals() {
            var contract = Build(5, 0, true);

            Assert.AreEqual(BaseTarget, Single(contract.GetGoals(0)));
        }

        [TestMethod]
        public void LeagueZero_WithBaseGoalsOnly_ReturnsBaseGoals() {
            var contract = Build(0, 0, true);

            Assert.AreEqual(BaseTarget, Single(contract.GetGoals(0)));
        }

        [TestMethod]
        public void LeagueInRange_WithGradeSpecs_ReturnsThatGradeSpec() {
            var contract = Build(5, 2, true);

            Assert.AreEqual(GradeSpecBase + 2, Single(contract.GetGoals(3)));
        }

        [TestMethod]
        public void LeagueInRange_WithGoalSetsOnly_ReturnsThatGoalSet() {
            var contract = Build(0, 2, true);

            Assert.AreEqual(GoalSetBase + 1, Single(contract.GetGoals(1)));
        }

        [TestMethod]
        public void LeagueAboveGradeSpecs_FallsThroughToGoalSetsThenGoals() {
            var withGoalSets = Build(5, 2, true);
            var withoutGoalSets = Build(5, 0, true);

            Assert.AreEqual(BaseTarget, Single(withGoalSets.GetGoals(7)));
            Assert.AreEqual(BaseTarget, Single(withoutGoalSets.GetGoals(7)));
        }

        [TestMethod]
        public void LocalContract_GradeUnset_LeagueOutOfRange_FallsBackToBaseGoals() {
            var contract = Build(5, 2, true);
            var local = new Ei.LocalContract { Grade = Ei.Contract.Types.PlayerGrade.GradeUnset, League = 9 };

            Assert.AreEqual(BaseTarget, Single(contract.GetGoals(local)));
        }

        [TestMethod]
        public void LocalContract_GradeSet_ReturnsThatGradeSpec() {
            var contract = Build(5, 2, true);
            var local = new Ei.LocalContract { Grade = Ei.Contract.Types.PlayerGrade.GradeAa, League = 0 };

            Assert.AreEqual(GradeSpecBase + 3, Single(contract.GetGoals(local)));
        }
    }
}
