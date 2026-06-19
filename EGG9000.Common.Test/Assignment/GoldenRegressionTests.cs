using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class GoldenRegressionTests {
        private sealed record Row(string Label, AccountFacts Facts, ContractFacts Contract, AssignmentSettings Settings, bool Expected);

        private static AssignmentSettings Force(PermanentRewardKind kind, ForceMode mode, double? floor = null) =>
            new() { ForceRules = new List<PermanentRewardRule> { new() { Kind = kind, Mode = mode, CsFloor = floor } } };

        private static IEnumerable<Row> Rows() {
            // Gates
            yield return new("gate: on break excluded",
                TestFactsBuilder.Account().OnBreak(true).Build(), TestFactsBuilder.Contract().Build(), new AssignmentSettings(), false);
            yield return new("gate: < 1k soul eggs excluded",
                TestFactsBuilder.Account().SoulEggs(999).Build(), TestFactsBuilder.Contract().Build(), new AssignmentSettings(), false);
            yield return new("gate: ultra without sub excluded",
                TestFactsBuilder.Account().HasSubscription(false).Build(), TestFactsBuilder.Contract().Ultra(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(), new AssignmentSettings(), false);

            // Baseline
            yield return new("eligible new contract, no filter -> assigned",
                TestFactsBuilder.Account().Build(), TestFactsBuilder.Contract().Build(), new AssignmentSettings(), true);

            // New reward filter
            yield return new("new filter no match -> excluded",
                TestFactsBuilder.Account().Grade(G.GradeC).Build(), TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Artifact } }, false);
            yield return new("new filter match -> assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).Build(), TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Gold } }, true);

            // Legacy fallback
            yield return new("legacy empty filter falls back to new -> assigned on match",
                TestFactsBuilder.Account().Grade(G.GradeC).Build(), TestFactsBuilder.Contract().Legacy(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                new AssignmentSettings { NewContractRewardFilter = new() { Ei.RewardType.Gold } }, true);

            // Colleggtible force beats a non-matching new filter
            yield return new("colleggtible force -> assigned despite filter",
                TestFactsBuilder.Account().MissingColleggtible(true).Build(),
                TestFactsBuilder.Contract().Colleggtible(true).Grade(G.GradeC, Ei.RewardType.Gold).Build(),
                Combine(Force(PermanentRewardKind.Colleggtible, ForceMode.AssignIfMissing), new() { Ei.RewardType.Artifact }), true);

            // Seasonal PE force
            yield return new("seasonal PE missing, assign-if-missing -> assigned",
                TestFactsBuilder.Account().MissingSeasonalPe(true).Build(), TestFactsBuilder.Contract().Seasonal(true).Build(),
                Force(PermanentRewardKind.SeasonalPe, ForceMode.AssignIfMissing), true);
            yield return new("seasonal PE not missing -> falls through, still assigned (no filter)",
                TestFactsBuilder.Account().MissingSeasonalPe(false).Build(), TestFactsBuilder.Contract().Seasonal(true).Build(),
                Force(PermanentRewardKind.SeasonalPe, ForceMode.AssignIfMissing), true);
            yield return new("seasonal PE below threshold under floor -> assigned",
                TestFactsBuilder.Account().MissingSeasonalPe(true).PreviousScore(100).Build(), TestFactsBuilder.Contract().Seasonal(true).Build(),
                Force(PermanentRewardKind.SeasonalPe, ForceMode.BelowThreshold, 5000), true);

            // Redo
            yield return new("redo No + previously completed -> excluded",
                TestFactsBuilder.Account().PreviouslyCompleted(true).Build(), TestFactsBuilder.Contract().Legacy(true).Build(),
                new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.No } }, false);
            yield return new("redo YesAll + previously completed -> assigned",
                TestFactsBuilder.Account().PreviouslyCompleted(true).Build(), TestFactsBuilder.Contract().Legacy(true).Build(),
                new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll } }, true);

            // Seasonal carve-out
            yield return new("redo YesAll but ExcludeSeasonal on seasonal replay -> excluded",
                TestFactsBuilder.Account().PreviouslyCompleted(true).Build(), TestFactsBuilder.Contract().Legacy(true).Seasonal(true).Build(),
                new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll, ExcludeSeasonal = true } }, false);

            // 2->3
            yield return new("2->3 disabled, completed two -> excluded",
                TestFactsBuilder.Account().Grade(G.GradeC).CompletedExactlyTwoGoals(true).Build(),
                TestFactsBuilder.Contract().Legacy(true).HadTwoRewards(true).Grade(G.GradeC, Ei.RewardType.Gold, Ei.RewardType.Boost, Ei.RewardType.Artifact).Build(),
                new AssignmentSettings { TwoToThree = false, Redo = new RedoRule { Mode = RedoLeggacyOption.No } }, false);
            yield return new("2->3 enabled, third reward matches -> assigned",
                TestFactsBuilder.Account().Grade(G.GradeC).CompletedExactlyTwoGoals(true).Build(),
                TestFactsBuilder.Contract().Legacy(true).HadTwoRewards(true).Grade(G.GradeC, Ei.RewardType.Gold, Ei.RewardType.Boost, Ei.RewardType.Artifact).Build(),
                new AssignmentSettings { TwoToThree = true, LegacyRewardFilter = new() { Ei.RewardType.Artifact }, Redo = new RedoRule { Mode = RedoLeggacyOption.No } }, true);
        }

        private static AssignmentSettings Combine(AssignmentSettings force, List<Ei.RewardType> newFilter) {
            force.NewContractRewardFilter = newFilter;
            return force;
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void GoldenTable_Holds() {
            foreach(var r in Rows()) {
                var d = AssignmentEvaluator.Evaluate(r.Facts, r.Contract, r.Settings);
                Assert.AreEqual(r.Expected, d.Assigned, r.Label);
            }
        }
    }
}
