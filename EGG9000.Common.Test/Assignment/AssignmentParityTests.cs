using EGG9000.Common.Contracts.Assignment.Diagnostics;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test.Assignment {
    // Full LegacyAssignmentDecision.Filter / AssignmentParityChecker.Compare runs require a complete
    // DBUser + CustomBackup graph (MessagePack-backed backups, protobuf contract Details). That is not
    // constructible in a focused unit test, so the engine-vs-legacy comparison is exercised manually via
    // /test parity against live data. Here we lock down the pure ExpectedSeasonalDeviation classifier,
    // which is the one piece of parity logic that does not need a graph.
    [TestClass]
    public class AssignmentParityTests {
        private static bool Classify(SeasonalPeOption option, bool missingPe) =>
            AssignmentParityChecker.ClassifySeasonalDeviation(option, () => missingPe);

        [TestMethod]
        [TestCategory("Unit")]
        public void DontAssign_IsAlwaysExpectedDeviation() {
            // Old excluded these unconditionally; new no longer skips. Always a flagged ruling deviation.
            Assert.IsTrue(Classify(SeasonalPeOption.DontAssign, missingPe: true));
            Assert.IsTrue(Classify(SeasonalPeOption.DontAssign, missingPe: false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AlwaysAssignIfMissing_DeviatesOnlyWhenNotMissing() {
            // Player satisfied (not missing): old excluded, new falls through -> expected deviation.
            Assert.IsTrue(Classify(SeasonalPeOption.AlwaysAssignIfMissing, missingPe: false));
            // Player still missing: both old and new include -> not a deviation.
            Assert.IsFalse(Classify(SeasonalPeOption.AlwaysAssignIfMissing, missingPe: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void AssignIfBelowThreshold_DeviatesOnlyWhenNotMissing() {
            Assert.IsTrue(Classify(SeasonalPeOption.AssignIfBelowThreshold, missingPe: false));
            Assert.IsFalse(Classify(SeasonalPeOption.AssignIfBelowThreshold, missingPe: true));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void NotSet_IsNeverADeviation() {
            Assert.IsFalse(Classify(SeasonalPeOption.NotSet, missingPe: true));
            Assert.IsFalse(Classify(SeasonalPeOption.NotSet, missingPe: false));
        }

        [TestMethod]
        [TestCategory("Unit")]
        public void MissingPe_NotEvaluatedForDontAssignOrNotSet() {
            // DontAssign returns true without consulting missingPe; NotSet returns false without it.
            // Pass a throwing delegate to prove neither path evaluates it.
            static bool ThrowIfCalled() => throw new System.InvalidOperationException("missingPe must not be evaluated");
            Assert.IsTrue(AssignmentParityChecker.ClassifySeasonalDeviation(SeasonalPeOption.DontAssign, ThrowIfCalled));
            Assert.IsFalse(AssignmentParityChecker.ClassifySeasonalDeviation(SeasonalPeOption.NotSet, ThrowIfCalled));
        }
    }
}
