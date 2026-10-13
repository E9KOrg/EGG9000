using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    public class GameDataResearchTests {
        private static readonly (string Id, int Levels, decimal Increase)[] Epic = [
            ("epic_internal_incubators", 20, 0.05M),
            ("epic_clucking", 20, 0.001M),
            ("epic_multiplier", 100, 2M),
            ("cheaper_research", 10, -0.05M),
            ("silo_capacity", 20, 6M),
            ("int_hatch_sharing", 10, 0.1M),
            ("int_hatch_calm", 20, 0.1M),
            ("soul_eggs", 140, 0.01M),
            ("epic_egg_laying", 20, 0.05M),
            ("transportation_lobbyist", 30, 0.05M),
            ("prophecy_bonus", 5, 0.01M),
        ];

        private static readonly (string Id, int Levels, decimal Increase, int Tier)[] Common = [
            ("comfy_nests", 50, 0.1M, 1),
            ("nutritional_sup", 40, 0.25M, 1),
            ("excitable_chickens", 25, 0.001M, 1),
            ("hab_capacity1", 8, 0.05M, 2),
            ("internal_hatchery1", 10, 2M, 2),
            ("padded_packaging", 30, 0.25M, 2),
            ("bigger_eggs", 1, 1M, 2),
            ("internal_hatchery2", 10, 5M, 3),
            ("leafsprings", 30, 0.05M, 3),
            ("vehicle_reliablity", 2, 1M, 3),
            ("coordinated_clucking", 50, 0.2M, 3),
            ("usde_prime", 1, 2M, 4),
            ("hen_house_ac", 50, 0.05M, 4),
            ("superfeed", 35, 0.25M, 4),
            ("microlux", 10, 0.05M, 4),
            ("lightweight_boxes", 40, 0.1M, 5),
            ("excoskeletons", 2, 1M, 5),
            ("internal_hatchery3", 15, 10M, 5),
            ("improved_genetics", 30, 0.15M, 5),
            ("traffic_management", 2, 1M, 6),
            ("motivational_clucking", 50, 0.5M, 6),
            ("driver_training", 30, 0.05M, 6),
            ("shell_fortification", 60, 0.15M, 6),
            ("egg_loading_bots", 2, 1M, 7),
            ("super_alloy", 50, 0.05M, 7),
            ("even_bigger_eggs", 5, 2M, 7),
            ("internal_hatchery4", 30, 25M, 7),
            ("quantum_storage", 20, 0.05M, 8),
            ("genetic_purification", 100, 0.1M, 8),
            ("internal_hatchery5", 250, 5M, 8),
            ("time_compress", 20, 0.1M, 8),
            ("hover_upgrades", 25, 0.05M, 9),
            ("graviton_coating", 7, 2M, 9),
            ("grav_plating", 25, 0.02M, 9),
            ("chrystal_shells", 100, 0.25M, 9),
            ("autonomous_vehicles", 5, 1M, 10),
            ("neural_linking", 30, 50M, 10),
            ("telepathic_will", 50, 0.25M, 10),
            ("enlightened_chickens", 150, 2M, 10),
            ("dark_containment", 25, 0.05M, 11),
            ("atomic_purification", 50, 0.1M, 11),
            ("multi_layering", 3, 10M, 11),
            ("timeline_diversion", 50, 0.02M, 11),
            ("wormhole_dampening", 25, 0.02M, 12),
            ("eggsistor", 100, 0.05M, 12),
            ("micro_coupling", 5, 1M, 12),
            ("neural_net_refine", 25, 0.05M, 12),
            ("matter_reconfig", 500, 0.01M, 13),
            ("timeline_splicing", 1, 10M, 13),
            ("hyper_portalling", 25, 0.05M, 13),
            ("relativity_optimization", 10, 0.1M, 13),
        ];

        private static readonly (string Id, int First, int Last, int Levels)[] EpicCosts = [
            ("hold_to_hatch", 10, 500, 15),
            ("epic_hatchery", 2, 1000, 20),
            ("silo_capacity", 5, 2000, 20),
            ("accounting_tricks", 100, 10000, 20),
            ("epic_internal_incubators", 50, 10000, 20),
            ("cheaper_contractors", 50, 4000, 10),
            ("bust_unions", 50, 4000, 10),
            ("cheaper_research", 100, 8000, 10),
            ("epic_clucking", 100, 10000, 20),
            ("epic_multiplier", 50, 10000, 100),
            ("drone_rewards", 50, 2000, 20),
            ("video_doubler_time", 100, 5000, 12),
            ("int_hatch_calm", 250, 10000, 20),
            ("soul_eggs", 500, 5000, 140),
            ("prestige_bonus", 2000, 20000, 20),
            ("epic_egg_laying", 250, 50000, 20),
            ("transportation_lobbyist", 250, 5000, 30),
            ("int_hatch_sharing", 100, 2500, 10),
            ("hold_to_research", 5000, 20000, 20),
            ("prophecy_bonus", 100000, 1000000, 5),
            ("afx_mission_time", 5000, 200000, 60),
            ("afx_mission_capacity", 50000, 1000000, 10),
        ];

        [TestMethod]
        public void EpicResearchListMatchesEggSharp() {
            Assert.HasCount(Epic.Length, Research.EpicResearchList);
            foreach(var (id, levels, increase) in Epic) {
                var item = Research.EpicResearchList.Single(x => x.id == id);
                Assert.AreEqual(levels, item.Levels, id);
                Assert.AreEqual(increase, item.Increase, id);
            }
        }

        [TestMethod]
        public void CommonResearchListMatchesEggSharp() {
            Assert.HasCount(Common.Length, Research.CommonResearchList);
            foreach(var (id, levels, increase, _) in Common) {
                var item = Research.CommonResearchList.Single(x => x.id == id);
                Assert.AreEqual(levels, item.Levels, id);
                Assert.AreEqual(increase, item.Increase, id);
            }
        }

        [TestMethod]
        public void ResearchJsonTakesEggSharpLevelsTiersAndMagnitudes() {
            var data = EiResearch.Get();
            foreach(var (id, levels, increase, tier) in Common) {
                var item = data.Single(x => x.Id == id);
                Assert.AreEqual(levels, item.Levels, id);
                Assert.AreEqual((double)increase, item.PerLevel, 1e-12, id);
                Assert.AreEqual(tier, item.Tier, id);
            }
            Assert.AreEqual(30, data.Single(x => x.Id == "video_doubler_time").PerLevel, 1e-12);
            Assert.AreEqual(6, data.Single(x => x.Id == "silo_capacity").PerLevel, 1e-12);
            Assert.AreEqual(20, data.Single(x => x.Id == "hold_to_research").Levels);
            Assert.AreEqual(60, data.Single(x => x.Id == "afx_mission_time").Levels);
        }

        [TestMethod]
        public void EpicResearchCostsMatchEggSharp() {
            var items = EiEpicResearch.Get().epicResearchItems;
            foreach(var (id, first, last, levels) in EpicCosts) {
                var item = items.Single(x => x.id == id);
                Assert.AreEqual(first, item._firstCost, id);
                Assert.AreEqual(last, item._lastCost, id);
                Assert.AreEqual(levels, item._numLevels, id);
                Assert.AreEqual(first, item.Costs.First(), id);
                Assert.AreEqual(last, item.Costs.Last(), id);
            }
        }

        [TestMethod]
        public void EpicResearchRowsMissingFromEggSharpStayLocal() {
            var items = EiEpicResearch.Get().epicResearchItems;
            Assert.AreEqual("Hyperloop", items.Single(x => x.id == "nter_hyperloop_trains").title);
            Assert.AreEqual("Fuel Tank", items.Single(x => x.id == "nter_fuel_tank").title);
            CollectionAssert.AreEqual(new[] { 50000, 250000, 1000000, 2000000, 3000000, 4000000, 5000000 }, items.Single(x => x.id == "nter_fuel_tank").Costs);
        }

        [TestMethod]
        public void GetCommonResearchCostMultiplierAppliesNegativeMagnitude() {
            var epic = new System.Collections.Generic.List<EGG9000.Common.Database.CustomResearch> {
                new() { Id = "cheaper_research", Level = 10 }
            };
            Assert.AreEqual(0.5, Research.GetCommonResearchCostMultiplier(null, epic), 1e-12);
        }
    }
}
