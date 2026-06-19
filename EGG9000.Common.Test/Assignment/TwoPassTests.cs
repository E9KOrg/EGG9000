using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Common.Test.Assignment {
    [TestClass]
    public class TwoPassTests {
        [TestMethod]
        [TestCategory("Unit")]
        public void YesOtherAccountMatch_IncludedWhenSiblingAssigned() {
            var contract = TestFactsBuilder.Contract().Legacy(true).Build();
            var aFacts = TestFactsBuilder.Account().Grade(G.GradeC).BoardingGroup(1).PreviouslyCompleted(true).Build();
            var aSettings = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll } };
            var bFacts = TestFactsBuilder.Account().Grade(G.GradeC).BoardingGroup(1).PreviouslyCompleted(true).Build();
            var bSettings = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesOtherAccountMatch } };

            var results = AssignmentEvaluator.EvaluateUser(
                new List<(AccountFacts, AssignmentSettings)> { (aFacts, aSettings), (bFacts, bSettings) }, contract);

            Assert.IsTrue(results.Single(r => ReferenceEquals(r.facts, bFacts)).decision.Assigned);
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void YesOtherAccountMatch_ExcludedWhenSiblingDifferentGroup() {
            var contract = TestFactsBuilder.Contract().Legacy(true).Build();
            var aFacts = TestFactsBuilder.Account().Grade(G.GradeC).BoardingGroup(2).PreviouslyCompleted(true).Build();
            var aSettings = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesAll } };
            var bFacts = TestFactsBuilder.Account().Grade(G.GradeC).BoardingGroup(1).PreviouslyCompleted(true).Build();
            var bSettings = new AssignmentSettings { Redo = new RedoRule { Mode = RedoLeggacyOption.YesOtherAccountMatch } };

            var results = AssignmentEvaluator.EvaluateUser(
                new List<(AccountFacts, AssignmentSettings)> { (aFacts, aSettings), (bFacts, bSettings) }, contract);

            Assert.IsFalse(results.Single(r => ReferenceEquals(r.facts, bFacts)).decision.Assigned);
        }
    }
}
