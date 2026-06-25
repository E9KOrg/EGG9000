using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test.Assignment {
    // Locks the end-to-end v2 decision for a spread of representative scenarios. Each row runs through
    // AssignmentEvaluator.Evaluate and asserts the final Assigned bit.
    [TestClass]
    public class GoldenRegressionTests {
        private sealed record Case(string Label, AccountFacts Facts, ContractFacts Contract, AssignmentSettings Settings, bool ExpectedAssigned);

        private static AssignmentSettings Colleggtible(ForceMode mode) {
            var s = new AssignmentSettings();
            s.SetForce(PermanentRewardKind.Colleggtible, mode);
            return s;
        }

        private static IEnumerable<Case> Cases() {
            var goldContract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();
            AccountFacts Eligible() => TestFactsBuilder.Account().Grade(G.GradeC).Build();

            yield return new("gate excludes (on break)",
                TestFactsBuilder.Account().Grade(G.GradeC).OnBreak(true).Build(),
                goldContract, new AssignmentSettings(), false);

            yield return new("eligible assigned",
                Eligible(), goldContract, new AssignmentSettings(), true);

            yield return new("reward filter match",
                Eligible(), goldContract,
                new AssignmentSettings { RewardFilter = new() { Ei.RewardType.Gold } }, true);

            yield return new("reward filter no match",
                Eligible(), goldContract,
                new AssignmentSettings { RewardFilter = new() { Ei.RewardType.Artifact } }, false);

            yield return new("colleggtible force (missing, enabled)",
                TestFactsBuilder.Account().Grade(G.GradeC).MissingColleggtible(true).Build(),
                TestFactsBuilder.Contract().Colleggtible(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                Colleggtible(ForceMode.AssignIfMissing), true);

            yield return new("seasonal AlwaysAssign -> assigned",
                Eligible(),
                TestFactsBuilder.Contract().Seasonal(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                new AssignmentSettings { Seasonal = new SeasonalRule { Mode = SeasonalMode.AlwaysAssign } }, true);

            yield return new("seasonal UntilPe missing -> assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).MissingSeasonalPe(true).Build(),
                TestFactsBuilder.Contract().Seasonal(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                new AssignmentSettings { Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilPeEarned } }, true);

            yield return new("seasonal UntilPe earned + after=false -> not assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).MissingSeasonalPe(false).Build(),
                TestFactsBuilder.Contract().Seasonal(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                new AssignmentSettings { Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilPeEarned, RewardFilterAfter = false } }, false);

            yield return new("seasonal UntilCsGoal below -> assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).PreviousScore(4000).Build(),
                TestFactsBuilder.Contract().Seasonal(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                new AssignmentSettings { Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilCsGoal, CsGoal = 5000 } }, true);

            yield return new("redo No + completed -> not assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).PreviouslyCompleted(true).Build(),
                goldContract,
                new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.No } }, false);

            yield return new("redo YesAll + completed -> assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).PreviouslyCompleted(true).Build(),
                goldContract,
                new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll } }, true);

            // Seasonal rule must NOT force-include for ExcludeSeasonal to be consulted in the Include
            // tier: UntilPe earned + after=true -> NotApplicable, falling through to the redo check.
            yield return new("ExcludeSeasonal on seasonal replay -> not assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).MissingSeasonalPe(false).PreviouslyCompleted(true).Build(),
                TestFactsBuilder.Contract().Seasonal(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                new AssignmentSettings {
                    Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilPeEarned, RewardFilterAfter = true },
                    Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll, ExcludeSeasonal = true }
                }, false);

            yield return new("2->3 disabled -> not assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).CompletedExactlyTwoGoals(true).Build(),
                TestFactsBuilder.Contract().HadTwoRewards(true).Grade(G.GradeC, Ei.RewardType.Gold, Ei.RewardType.Cash, Ei.RewardType.Artifact).Build(),
                new AssignmentSettings { TwoToThree = false }, false);

            yield return new("2->3 enabled + third reward matches -> assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).CompletedExactlyTwoGoals(true).Build(),
                TestFactsBuilder.Contract().HadTwoRewards(true).Grade(G.GradeC, Ei.RewardType.Gold, Ei.RewardType.Cash, Ei.RewardType.Artifact).Build(),
                new AssignmentSettings { TwoToThree = true, RewardFilter = new() { Ei.RewardType.Artifact } }, true);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GoldenTable_LocksV2Behavior() {
            foreach(var c in Cases()) {
                var decision = AssignmentEvaluator.Evaluate(c.Facts, c.Contract, c.Settings);
                Assert.AreEqual(c.ExpectedAssigned, decision.Assigned, $"case: {c.Label}");
            }
        }
    }
}
