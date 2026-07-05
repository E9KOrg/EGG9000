using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class TwoPassTests {
        private static AssignmentSettings YesOtherAccountMatch() {
            return new() { Redo = new RedoRule { Mode = RedoLeggacyOption.YesOtherAccountMatch } };
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void YesOtherAccountMatch_IncludedWhenSiblingAssignedSameGroup() {
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();

            // The replay account: previously completed, mode YesOtherAccountMatch, group 1.
            var replay = TestFactsBuilder.Account().AccountId("replay").Grade(G.GradeC).BoardingGroup(1).PreviouslyCompleted(true).Build();
            // The sibling: eligible, fresh, same grade + group -> will be assigned in pass 1.
            var sibling = TestFactsBuilder.Account().AccountId("sibling").Grade(G.GradeC).BoardingGroup(1).PreviouslyCompleted(false).Build();

            var inputs = new List<(AccountFacts, AssignmentSettings)> {
                (replay, YesOtherAccountMatch()),
                (sibling, new AssignmentSettings())
            };

            var results = AssignmentEvaluator.EvaluateUser(inputs, contract);
            var replayDecision = results.First(r => r.facts.AccountId == "replay").decision;
            var siblingDecision = results.First(r => r.facts.AccountId == "sibling").decision;

            Assert.IsTrue(siblingDecision.Assigned, "sibling should be assigned outright");
            Assert.IsTrue(replayDecision.Assigned, "replay should be pulled in by the assigned sibling");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void YesOtherAccountMatch_ExcludedWhenSiblingDifferentGroup() {
            var contract = TestFactsBuilder.Contract().Grade(G.GradeC, Ei.RewardType.Gold).Build();

            var replay = TestFactsBuilder.Account().AccountId("replay").Grade(G.GradeC).BoardingGroup(1).PreviouslyCompleted(true).Build();
            var sibling = TestFactsBuilder.Account().AccountId("sibling").Grade(G.GradeC).BoardingGroup(2).PreviouslyCompleted(false).Build();

            var inputs = new List<(AccountFacts, AssignmentSettings)> {
                (replay, YesOtherAccountMatch()),
                (sibling, new AssignmentSettings())
            };

            var results = AssignmentEvaluator.EvaluateUser(inputs, contract);
            var replayDecision = results.First(r => r.facts.AccountId == "replay").decision;
            var siblingDecision = results.First(r => r.facts.AccountId == "sibling").decision;

            Assert.IsTrue(siblingDecision.Assigned);
            Assert.IsFalse(replayDecision.Assigned, "different boarding group means no sibling match");
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void YesOtherAccountMatch_IncludedWhenSiblingForcedBySeasonal() {
            var contract = TestFactsBuilder.Contract().Seasonal(true).Grade(G.GradeC, Ei.RewardType.Gold).Build();

            // The replay account: seasonal goal already met (Force tier would Exclude it outright),
            // group 1, opted into YesOtherAccountMatch.
            var replay = TestFactsBuilder.Account().AccountId("replay").Grade(G.GradeC).BoardingGroup(1)
                .MissingSeasonalPe(false).Build();
            var replaySettings = new AssignmentSettings {
                Redo = new RedoRule { Mode = RedoLeggacyOption.YesOtherAccountMatch },
                Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilPeEarned, RewardFilterAfter = false }
            };

            // The sibling: still missing seasonal PE -> force-assigned in pass 1, same grade + group.
            var sibling = TestFactsBuilder.Account().AccountId("sibling").Grade(G.GradeC).BoardingGroup(1)
                .MissingSeasonalPe(true).Build();
            var siblingSettings = new AssignmentSettings {
                Seasonal = new SeasonalRule { Mode = SeasonalMode.UntilPeEarned }
            };

            var inputs = new List<(AccountFacts, AssignmentSettings)> {
                (replay, replaySettings),
                (sibling, siblingSettings)
            };

            var results = AssignmentEvaluator.EvaluateUser(inputs, contract);
            var replayDecision = results.First(r => r.facts.AccountId == "replay").decision;
            var siblingDecision = results.First(r => r.facts.AccountId == "sibling").decision;

            Assert.IsTrue(siblingDecision.Assigned, "sibling should be force-assigned by the seasonal rule");
            Assert.IsTrue(replayDecision.Assigned, "replay should be pulled in by the seasonal-forced sibling");
        }
    }
}
