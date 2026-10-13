using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class HabCapacityTests {
        private static IEnumerable<object[]> Capacities => [
            [0, 250d],
            [1, 500d],
            [2, 1e3],
            [3, 2e3],
            [4, 5e3],
            [5, 1e4],
            [6, 2e4],
            [7, 5e4],
            [8, 1e5],
            [9, 2e5],
            [10, 5e5],
            [11, 1e6],
            [12, 2e6],
            [13, 5e6],
            [14, 1e7],
            [15, 2.5e7],
            [16, 5e7],
            [17, 1e8],
            [18, 6e8],
        ];

        [TestMethod]
        [DynamicData(nameof(Capacities))]
        public void Base_capacity_matches_legacy_table(int habId, double expected) {
            Assert.AreEqual(expected, EggIncHabSpace.BaseCapacity(habId));
        }

        [TestMethod]
        public void Unknown_hab_contributes_nothing() {
            Assert.AreEqual(0d, EggIncHabSpace.BaseCapacity(19));
        }

        [TestMethod]
        public void Farm_sums_every_hab_slot() {
            var farm = new CustomFarm {
                Habs = [18, 18, 17, 19],
            };
            Assert.AreEqual(1.3e9, EggIncHabSpace.GetBaseHabSpace(farm));
        }

        [TestMethod]
        public void Missing_farm_or_habs_is_zero() {
            Assert.AreEqual(0d, EggIncHabSpace.GetBaseHabSpace(null));
            Assert.AreEqual(0d, EggIncHabSpace.GetBaseHabSpace(new CustomFarm()));
        }
    }
}
