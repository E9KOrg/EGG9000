using EGG9000.Common.Services;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class LatencyRingTests {
        [TestMethod]
        public void EmptyRingIsAllZero() {
            var ring = new LatencyRing();
            Assert.AreEqual(0, ring.Count);
            Assert.AreEqual((0L, 0L, 0L, 0L), ring.Percentiles());
        }

        [TestMethod]
        public void PartialFillUsesOnlyWrittenSlots() {
            var ring = new LatencyRing();
            for(var i = 1; i <= 100; i++) ring.Add(i);
            Assert.AreEqual(100, ring.Count);
            var (p50, p95, p99, max) = ring.Percentiles();
            Assert.AreEqual(50L, p50);
            Assert.AreEqual(95L, p95);
            Assert.AreEqual(99L, p99);
            Assert.AreEqual(100L, max);
        }

        [TestMethod]
        public void SingleSampleReportsItselfEverywhere() {
            var ring = new LatencyRing();
            ring.Add(42);
            Assert.AreEqual(1, ring.Count);
            Assert.AreEqual((42L, 42L, 42L, 42L), ring.Percentiles());
        }

        [TestMethod]
        public void OverwritesOldestOnceFull() {
            var ring = new LatencyRing();
            for(var i = 0; i < LatencyRing.Capacity; i++) ring.Add(1);
            Assert.AreEqual(LatencyRing.Capacity, ring.Count);
            Assert.AreEqual((1L, 1L, 1L, 1L), ring.Percentiles());

            for(var i = 0; i < LatencyRing.Capacity; i++) ring.Add(7);
            Assert.AreEqual(LatencyRing.Capacity, ring.Count);
            Assert.AreEqual((7L, 7L, 7L, 7L), ring.Percentiles());
        }

        [TestMethod]
        public void WrapKeepsNewestWindow() {
            var ring = new LatencyRing();
            for(var i = 1; i <= LatencyRing.Capacity + 100; i++) ring.Add(i);
            var (p50, _, _, max) = ring.Percentiles();
            Assert.AreEqual(LatencyRing.Capacity + 100L, max);
            Assert.IsTrue(p50 > 100, $"expected newest window, got p50 {p50}");
        }

        [TestMethod]
        public void PercentilesIgnoreInsertionOrder() {
            var ring = new LatencyRing();
            long[] samples = [9, 1, 8, 2, 7, 3, 6, 4, 5, 10];
            foreach(var s in samples) ring.Add(s);
            var (p50, p95, p99, max) = ring.Percentiles();
            Assert.AreEqual(5L, p50);
            Assert.AreEqual(10L, p95);
            Assert.AreEqual(10L, p99);
            Assert.AreEqual(10L, max);
        }
    }
}
