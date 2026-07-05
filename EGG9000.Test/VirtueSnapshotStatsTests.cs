using System.Collections.Generic;

using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class VirtueSnapshotStatsTests {
        private static VirtueSnapshotStats Sample() => new VirtueSnapshotStats {
            CurrentEgg = Ei.Egg.Integrity,
            Delivered = new Dictionary<Ei.Egg, double> {
                [Ei.Egg.Curiosity] = 1.5e9,
                [Ei.Egg.Integrity] = 2.5e9,
                [Ei.Egg.Humility] = 0,
                [Ei.Egg.Resilience] = 0,
                [Ei.Egg.Kindness] = 0,
            },
            TeTotal = 10,
            TeEarned = 7,
            TePending = 3,
            ShiftCount = 2,
            Resets = 1,
        };

        [TestMethod]
        public void Equals_SameValues_DifferentDictionaryInsertionOrder_ReturnsTrue() {
            var a = Sample();
            var b = Sample();
            b.Delivered = new Dictionary<Ei.Egg, double> {
                [Ei.Egg.Kindness] = 0,
                [Ei.Egg.Resilience] = 0,
                [Ei.Egg.Humility] = 0,
                [Ei.Egg.Integrity] = 2.5e9,
                [Ei.Egg.Curiosity] = 1.5e9,
            };

            Assert.IsTrue(a.Equals(b));
        }

        [TestMethod]
        public void Equals_DifferentDeliveredValue_ReturnsFalse() {
            var a = Sample();
            var b = Sample();
            b.Delivered[Ei.Egg.Curiosity] = 999;

            Assert.IsFalse(a.Equals(b));
        }

        [TestMethod]
        public void Equals_Null_ReturnsFalse() {
            Assert.IsFalse(Sample().Equals(null));
        }

        [TestMethod]
        public void UserSnapShot_VirtueStats_RoundTripsThroughJson() {
            var snapshot = new UserSnapShot();
            var stats = Sample();

            snapshot.VirtueStats = stats;
            var roundTripped = new UserSnapShot { VirtueStatsJson = snapshot.VirtueStatsJson }.VirtueStats;

            Assert.IsTrue(stats.Equals(roundTripped));
            Assert.AreEqual(Ei.Egg.Integrity, roundTripped.CurrentEgg);
        }
    }
}
