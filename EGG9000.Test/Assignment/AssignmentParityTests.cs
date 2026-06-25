using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Contracts.Assignment.Diagnostics;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Assignment {
    [TestClass]
    public class AssignmentParityTests {
        // v2 classifier: expected (by-design) seasonal divergences are DontAssign-while-missing (old
        // excluded, new force-assigns) and NotSet-while-satisfied (old assigned, new stops after PE).
        // AlwaysAssignIfMissing / AssignIfBelowThreshold map 1:1 -> never a divergence.

        [TestMethod]
        [TestCategory("Unit")]
        public void DontAssign_ExpectedOnlyWhileMissing() {
            Assert.IsTrue(AssignmentParityChecker.ClassifySeasonalDeviation(SeasonalPeOption.DontAssign, () => true));
            Assert.IsFalse(AssignmentParityChecker.ClassifySeasonalDeviation(SeasonalPeOption.DontAssign, () => false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NotSet_ExpectedOnlyWhenSatisfied() {
            Assert.IsTrue(AssignmentParityChecker.ClassifySeasonalDeviation(SeasonalPeOption.NotSet, () => false));
            Assert.IsFalse(AssignmentParityChecker.ClassifySeasonalDeviation(SeasonalPeOption.NotSet, () => true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void CleanlyMappedOptions_NeverDeviate() {
            foreach(var missing in new[] { true, false }) {
                Assert.IsFalse(AssignmentParityChecker.ClassifySeasonalDeviation(SeasonalPeOption.AlwaysAssignIfMissing, () => missing));
                Assert.IsFalse(AssignmentParityChecker.ClassifySeasonalDeviation(SeasonalPeOption.AssignIfBelowThreshold, () => missing));
            }
        }

        // New-only seasonal capabilities (AlwaysAssign, RewardFilterAfter) have no old-key equivalent, so
        // any diff they cause is by-design and must be flagged expected regardless of the migrated option.
        [TestMethod]
        [TestCategory("Unit")]
        public void NewOnlySeasonalCapabilities_FlaggedExpected() {
            var season = new EGG9000.Common.Database.Entities.SeasonInfo { Id = "s1" };
            var progresses = new System.Collections.Generic.List<EGG9000.Common.Database.Entities.UserSeasonProgress>();

            var always = new EGG9000.Common.Database.Entities.EggIncAccount {
                Id = "u", SeasonalPeOption = SeasonalPeOption.AlwaysAssignIfMissing,
                Assignment = new EGG9000.Common.Contracts.Assignment.AssignmentSettings {
                    Seasonal = new EGG9000.Common.Contracts.Assignment.SeasonalRule { Mode = SeasonalMode.AlwaysAssign }
                }
            };
            Assert.IsTrue(AssignmentParityChecker.IsExpectedSeasonalDeviation(always, season, progresses));

            var after = new EGG9000.Common.Database.Entities.EggIncAccount {
                Id = "u2", SeasonalPeOption = SeasonalPeOption.AlwaysAssignIfMissing,
                Assignment = new EGG9000.Common.Contracts.Assignment.AssignmentSettings {
                    Seasonal = new EGG9000.Common.Contracts.Assignment.SeasonalRule { Mode = SeasonalMode.UntilPeEarned, RewardFilterAfter = true }
                }
            };
            Assert.IsTrue(AssignmentParityChecker.IsExpectedSeasonalDeviation(after, season, progresses));
        }
    }
}
