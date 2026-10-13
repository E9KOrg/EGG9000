using EGG9000.Common.JsonData;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class EiStaticsGameDataTests {
        private static IEnumerable<object[]> EggValues => [
            [1, 0.25],
            [2, 1.25],
            [3, 6.25],
            [4, 30d],
            [5, 150d],
            [6, 700d],
            [7, 3000d],
            [8, 12500d],
            [9, 50000d],
            [10, 175000d],
            [11, 525000d],
            [12, 1.5e6],
            [13, 1e7],
            [14, 1e9],
            [15, 1e11],
            [16, 1e12],
            [17, 1.5e13],
            [18, 1e14],
            [19, 1e-7],
            [50, 1d],
            [51, 1d],
            [52, 1d],
            [53, 1d],
            [54, 1d],
            [100, 5d],
            [101, 0.05],
            [102, 0.1],
            [103, 4.99],
            [104, 0.99],
            [200, 4.99],
        ];

        private static IEnumerable<object[]> Boosts => [
            ["quantum_bulb", 10, 0, 10, 0],
            ["jimbos_blue", 60, 2, 50, 1],
            ["jimbos_blue_v2", 20, 3, 50, 0],
            ["jimbos_blue_big", 240, 3, 100, 1],
            ["jimbos_purple", 30, 10, 250, 2],
            ["jimbos_purple_v2", 15, 10, 150, 1],
            ["jimbos_purple_big", 180, 10, 750, 1],
            ["jimbos_orange", 10, 50, 2500, 2],
            ["jimbos_orange_big", 120, 50, 7500, 3],
            ["tachyon_prism_blue", 30, 10, 200, 1],
            ["tachyon_prism_blue_v2", 10, 10, 50, 0],
            ["tachyon_prism_blue_big", 240, 10, 500, 0],
            ["tachyon_prism_purple", 20, 100, 1000, 3],
            ["tachyon_prism_purple_v2", 10, 100, 1000, 2],
            ["tachyon_prism_purple_big", 120, 100, 5000, 2],
            ["tachyon_prism_orange", 10, 1000, 12000, 4],
            ["tachyon_prism_orange_big", 60, 1000, 25000, 4],
            ["boost_beacon_blue", 30, 2, 1000, 1],
            ["boost_beacon_purple", 10, 10, 8000, 4],
            ["boost_beacon_blue_big", 60, 5, 15000, 3],
            ["boost_beacon_orange", 10, 50, 50000, 8],
            ["soul_beacon_blue", 60, 5, 200, 0],
            ["soul_beacon_blue_v2", 30, 5, 200, 0],
            ["soul_beacon_purple", 30, 50, 1000, 0],
            ["soul_beacon_purple_v2", 20, 50, 1000, 0],
            ["soul_beacon_orange", 10, 500, 10000, 0],
            ["subsidy_application", 0, 10, 200, 1],
            ["blank_check", 0, 100, 750, 1],
            ["money_printer", 0, 500, 2500, 2],
            ["soul_mirror_blue", 10, 1, 100, 1],
            ["soul_mirror_purple", 60, 1, 500, 2],
            ["soul_mirror_orange", 1440, 1, 2000, 3],
            ["dilithium_bulb", 10, 1, 1000, 0],
        ];

        [TestMethod]
        [DynamicData(nameof(EggValues))]
        public void Egg_value_comes_from_game_data(int id, double expected) {
            var egg = Root.Get().eggIncEggs.Single(e => e.id == id);
            Assert.AreEqual(expected, egg.value);
        }

        [TestMethod]
        [DynamicData(nameof(Boosts))]
        public void Boost_fields_come_from_game_data(string id, int minutes, int value, int goldenEggs, int tokens) {
            var boost = Root.Get().eggIncBoosts.Single(b => b.id == id);
            Assert.AreEqual(minutes, boost.timeMinutesInt);
            Assert.AreEqual(value, boost.value);
            Assert.AreEqual(goldenEggs, boost.costGoldenEggs);
            Assert.AreEqual(tokens, boost.costTokens);
        }

        [TestMethod]
        public void Every_local_row_is_covered() {
            Assert.HasCount(EggValues.Count(), Root.Get().eggIncEggs);
            Assert.HasCount(Boosts.Count(), Root.Get().eggIncBoosts);
        }
    }
}
