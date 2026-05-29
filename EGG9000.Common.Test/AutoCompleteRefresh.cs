using EGG9000.Common.Commands;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

namespace EGG9000.Common.Test {
    [TestClass]
    public class AutoCompleteRefreshTest {
        // Unique key per test - the rate limiter holds process-wide static state.
        private static string Key() => "test-" + Guid.NewGuid().ToString("N");

        [TestMethod]
        public void FirstRefreshAllowed() {
            Assert.IsTrue(AutoCompleteRefresh.TryMarkRefresh(Key()));
        }

        [TestMethod]
        public void SecondImmediateRefreshRateLimited() {
            var key = Key();
            Assert.IsTrue(AutoCompleteRefresh.TryMarkRefresh(key));
            Assert.IsFalse(AutoCompleteRefresh.TryMarkRefresh(key));
        }

        [TestMethod]
        public void DifferentKeysAreIndependent() {
            Assert.IsTrue(AutoCompleteRefresh.TryMarkRefresh(Key()));
            Assert.IsTrue(AutoCompleteRefresh.TryMarkRefresh(Key()));
        }

        [TestMethod]
        public void SecondsUntilAllowedZeroBeforeAnyRefresh() {
            Assert.AreEqual(0, AutoCompleteRefresh.SecondsUntilAllowed(Key()));
        }

        [TestMethod]
        public void SecondsUntilAllowedPositiveAfterRefresh() {
            var key = Key();
            AutoCompleteRefresh.TryMarkRefresh(key);
            var remaining = AutoCompleteRefresh.SecondsUntilAllowed(key);
            Assert.IsTrue(remaining is > 0 and <= 30, $"expected 1-30, got {remaining}");
        }
    }
}
