using EGG9000.Common.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ArtifactCatalogTests {
        private static readonly long[] XpThresholds = [
            0, 500, 3000, 8000, 18000, 43000, 93000, 193000, 443000, 943000,
            1943000, 3943000, 7943000, 15943000, 30943000, 50943000, 85943000, 145943000, 245943000, 395943000,
            595943000, 845943000, 1145943000, 1470943000, 1820943000, 2220943000, 2720943000, 3320943000, 4070943000, 5070943000,
        ];

        private static readonly double[] RarityMultipliers = [
            1.0, 1.0499999523162842, 1.100000023841858, 1.149999976158142, 1.2000000476837158,
            1.25, 1.2999999523162842, 1.350000023841858, 1.399999976158142, 1.4500000476837158,
            1.5, 1.5499999523162842, 1.600000023841858, 1.649999976158142, 1.7000000476837158,
            1.75, 1.850000023841858, 2.0, 2.25, 2.5,
            3.0, 3.5, 4.0, 4.5, 5.0,
            6.0, 7.0, 8.0, 9.0, 10.0,
        ];

        private static readonly (int AfxId, int Tier, double Rare, double Epic, double Legendary)[] Coefficients = [
            (0, 2, 30, 0, 0), (0, 3, 20, 0, 0), (0, 4, 30, 250, 1500), (3, 2, 12, 0, 0), (3, 3, 0, 150, 0), (3, 4, 40, 200, 1000),
            (4, 3, 11, 0, 0), (4, 4, 50, 0, 1500), (5, 2, 15, 0, 0), (5, 3, 10, 0, 0), (5, 4, 0, 100, 1000), (6, 2, 30, 0, 0),
            (6, 3, 20, 100, 0), (6, 4, 40, 140, 1000), (7, 2, 10, 0, 0), (7, 3, 0, 100, 0), (7, 4, 40, 0, 1000), (8, 2, 0, 100, 0),
            (8, 3, 10, 0, 0), (8, 4, 0, 150, 1000), (9, 2, 0, 80, 0), (9, 3, 10, 100, 0), (9, 4, 0, 150, 1000), (10, 3, 0, 100, 0),
            (10, 4, 0, 150, 1000), (11, 3, 10, 0, 0), (11, 4, 40, 0, 1000), (12, 2, 25, 0, 0), (12, 3, 10, 0, 1000), (12, 4, 40, 0, 1000),
            (21, 3, 10, 100, 0), (21, 4, 40, 180, 1000), (22, 4, 0, 170, 1000), (23, 2, 0, 80, 0), (23, 3, 10, 0, 0), (23, 4, 50, 170, 1000),
            (24, 2, 15, 0, 0), (24, 3, 10, 80, 0), (24, 4, 33, 160, 1000), (25, 3, 10, 0, 0), (25, 4, 100, 400, 1200), (26, 3, 10, 0, 0),
            (26, 4, 120, 500, 1200), (27, 3, 10, 0, 0), (27, 4, 40, 200, 1000), (28, 4, 0, 150, 1000), (29, 3, 10, 0, 0), (29, 4, 0, 200, 1000),
            (30, 2, 10, 0, 0), (30, 3, 10, 0, 0), (30, 4, 40, 250, 1000),
        ];

        private static readonly (int AfxId, int Tier, int Rarity, int Slots)[] StoneSlots = [
            (0, 1, 1, 0), (0, 2, 1, 0), (0, 2, 2, 1), (0, 3, 1, 0), (0, 3, 2, 1), (0, 4, 1, 0), (0, 4, 2, 1), (0, 4, 3, 2),
            (0, 4, 4, 3), (3, 1, 1, 0), (3, 2, 1, 0), (3, 2, 2, 1), (3, 3, 1, 0), (3, 3, 3, 2), (3, 4, 1, 0), (3, 4, 2, 1),
            (3, 4, 3, 2), (3, 4, 4, 3), (4, 1, 1, 0), (4, 2, 1, 0), (4, 3, 1, 0), (4, 3, 2, 1), (4, 4, 1, 0), (4, 4, 2, 1),
            (4, 4, 4, 2), (5, 1, 1, 0), (5, 2, 1, 0), (5, 2, 2, 1), (5, 3, 1, 0), (5, 3, 2, 1), (5, 4, 1, 0), (5, 4, 3, 2),
            (5, 4, 4, 3), (6, 1, 1, 0), (6, 2, 1, 0), (6, 2, 2, 1), (6, 3, 1, 0), (6, 3, 2, 1), (6, 3, 3, 2), (6, 4, 1, 0),
            (6, 4, 2, 1), (6, 4, 3, 2), (6, 4, 4, 3), (7, 1, 1, 0), (7, 2, 1, 0), (7, 2, 2, 1), (7, 3, 1, 0), (7, 3, 3, 1),
            (7, 4, 1, 0), (7, 4, 2, 1), (7, 4, 4, 2), (8, 1, 1, 0), (8, 2, 1, 0), (8, 2, 3, 2), (8, 3, 1, 0), (8, 3, 2, 1),
            (8, 4, 1, 0), (8, 4, 3, 2), (8, 4, 4, 3), (9, 1, 1, 0), (9, 2, 1, 0), (9, 2, 3, 2), (9, 3, 1, 0), (9, 3, 2, 1),
            (9, 3, 3, 2), (9, 4, 1, 0), (9, 4, 3, 2), (9, 4, 4, 3), (10, 1, 1, 0), (10, 2, 1, 0), (10, 3, 1, 0), (10, 3, 3, 1),
            (10, 4, 1, 0), (10, 4, 3, 1), (10, 4, 4, 2), (11, 1, 1, 0), (11, 2, 1, 0), (11, 3, 1, 0), (11, 3, 2, 1), (11, 4, 1, 0),
            (11, 4, 2, 1), (11, 4, 4, 2), (12, 1, 1, 0), (12, 2, 1, 0), (12, 2, 2, 1), (12, 3, 1, 0), (12, 3, 2, 1), (12, 3, 4, 3),
            (12, 4, 1, 0), (12, 4, 2, 1), (12, 4, 4, 3), (21, 1, 1, 0), (21, 2, 1, 0), (21, 3, 1, 0), (21, 3, 2, 1), (21, 3, 3, 2),
            (21, 4, 1, 0), (21, 4, 2, 1), (21, 4, 3, 2), (21, 4, 4, 3), (22, 1, 1, 0), (22, 2, 1, 0), (22, 3, 1, 0), (22, 4, 1, 0),
            (22, 4, 3, 1), (22, 4, 4, 2), (23, 1, 1, 0), (23, 2, 1, 0), (23, 2, 3, 2), (23, 3, 1, 0), (23, 3, 2, 1), (23, 4, 1, 0),
            (23, 4, 2, 1), (23, 4, 3, 2), (23, 4, 4, 3), (24, 1, 1, 0), (24, 2, 1, 0), (24, 2, 2, 1), (24, 3, 1, 0), (24, 3, 2, 1),
            (24, 3, 3, 2), (24, 4, 1, 0), (24, 4, 2, 1), (24, 4, 3, 2), (24, 4, 4, 3), (25, 1, 1, 0), (25, 2, 1, 0), (25, 3, 1, 0),
            (25, 3, 2, 1), (25, 4, 1, 0), (25, 4, 2, 1), (25, 4, 3, 2), (25, 4, 4, 2), (26, 1, 1, 0), (26, 2, 1, 0), (26, 3, 1, 0),
            (26, 3, 2, 1), (26, 4, 1, 0), (26, 4, 2, 1), (26, 4, 3, 2), (26, 4, 4, 2), (27, 1, 1, 0), (27, 2, 1, 0), (27, 3, 1, 0),
            (27, 3, 2, 1), (27, 4, 1, 0), (27, 4, 2, 1), (27, 4, 3, 2), (27, 4, 4, 2), (28, 1, 1, 0), (28, 2, 1, 0), (28, 3, 1, 0),
            (28, 4, 1, 0), (28, 4, 3, 2), (28, 4, 4, 3), (29, 1, 1, 0), (29, 2, 1, 0), (29, 3, 1, 0), (29, 3, 2, 1), (29, 4, 1, 0),
            (29, 4, 3, 1), (29, 4, 4, 2), (30, 1, 1, 0), (30, 2, 1, 0), (30, 2, 2, 1), (30, 3, 1, 0), (30, 3, 2, 1), (30, 4, 1, 0),
            (30, 4, 2, 1), (30, 4, 3, 2), (30, 4, 4, 2),
        ];

        private static readonly (int AfxId, int Level, double Initial, (int AfxId, int Level, int Count)[] Recipe)[] Recipes = [
            (0, 1, 553, [(0, 0, 3)]),
            (0, 2, 12234, [(0, 1, 6)]),
            (0, 3, 91819, [(0, 2, 6), (43, 1, 1)]),
            (3, 1, 3982, [(3, 0, 4)]),
            (3, 2, 16466, [(3, 1, 5), (18, 1, 1)]),
            (3, 3, 58753, [(3, 2, 6), (27, 2, 2)]),
            (4, 1, 6075, [(4, 0, 4)]),
            (4, 2, 23885, [(4, 1, 5), (18, 1, 1)]),
            (4, 3, 86083, [(4, 2, 6), (17, 2, 3)]),
            (5, 1, 67892, [(5, 0, 5), (18, 2, 1)]),
            (5, 2, 168121, [(5, 1, 7), (11, 2, 4)]),
            (5, 3, 330068, [(5, 2, 10), (11, 3, 4)]),
            (6, 1, 383, [(6, 0, 3)]),
            (6, 2, 7628, [(6, 1, 5), (9, 0, 2)]),
            (6, 3, 41267, [(6, 2, 6), (17, 2, 1)]),
            (7, 1, 3302, [(7, 0, 5)]),
            (7, 2, 26353, [(7, 1, 7), (43, 1, 2)]),
            (7, 3, 139253, [(7, 2, 8), (18, 2, 1)]),
            (8, 1, 5167, [(8, 0, 5)]),
            (8, 2, 28986, [(8, 1, 6), (30, 1, 2)]),
            (8, 3, 163774, [(8, 2, 8), (17, 2, 3)]),
            (9, 1, 8798, [(9, 0, 4), (17, 1, 1)]),
            (9, 2, 30365, [(9, 1, 6), (18, 1, 4)]),
            (9, 3, 139253, [(9, 2, 8), (4, 3, 2)]),
            (10, 1, 114405, [(10, 0, 6), (17, 2, 4)]),
            (10, 2, 360472, [(10, 1, 10), (9, 3, 3)]),
            (10, 3, 934701, [(10, 2, 12), (5, 3, 1)]),
            (11, 1, 16466, [(11, 0, 6), (18, 1, 1)]),
            (11, 2, 50473, [(11, 1, 10), (10, 0, 2)]),
            (11, 3, 237296, [(11, 2, 12), (10, 2, 1)]),
            (12, 1, 3982, [(12, 0, 6)]),
            (12, 2, 25099, [(12, 1, 7), (43, 1, 2)]),
            (12, 3, 110546, [(12, 2, 8), (17, 2, 4)]),
            (21, 1, 1186, [(21, 0, 5)]),
            (21, 2, 13827, [(21, 1, 7), (6, 2, 2)]),
            (21, 3, 58753, [(21, 2, 10), (17, 2, 2)]),
            (22, 1, 10082, [(22, 0, 5), (8, 0, 1)]),
            (22, 2, 39572, [(22, 1, 7), (4, 2, 1)]),
            (22, 3, 168121, [(22, 2, 9), (18, 2, 2)]),
            (23, 1, 90, [(23, 0, 3)]),
            (23, 2, 14672, [(23, 1, 7), (8, 0, 2)]),
            (23, 3, 110353, [(23, 2, 10), (17, 2, 2)]),
            (24, 1, 18400, [(24, 0, 5), (29, 0, 2)]),
            (24, 2, 67892, [(24, 1, 7), (30, 2, 4)]),
            (24, 3, 306621, [(24, 2, 9), (18, 2, 4)]),
            (25, 1, 36322, [(25, 0, 6), (6, 3, 2)]),
            (25, 2, 113895, [(25, 1, 9), (43, 2, 3)]),
            (25, 3, 442510, [(25, 2, 12), (10, 2, 2)]),
            (26, 1, 77412, [(26, 0, 6), (25, 1, 2)]),
            (26, 2, 259516, [(26, 1, 10), (24, 3, 1)]),
            (26, 3, 1220591, [(26, 2, 12), (25, 3, 4)]),
            (27, 1, 9425, [(27, 0, 6)]),
            (27, 2, 37923, [(27, 1, 8), (43, 1, 4)]),
            (27, 3, 226570, [(27, 2, 10), (18, 2, 4)]),
            (28, 1, 24487, [(28, 0, 5), (29, 0, 1)]),
            (28, 2, 80590, [(28, 1, 8), (43, 2, 2)]),
            (28, 3, 271970, [(28, 2, 10), (30, 3, 2)]),
            (29, 1, 26353, [(29, 0, 4), (43, 1, 4)]),
            (29, 2, 88920, [(29, 1, 6), (43, 2, 3)]),
            (29, 3, 215900, [(29, 2, 8), (24, 3, 3)]),
            (30, 1, 7084, [(30, 0, 6)]),
            (30, 2, 30365, [(30, 1, 8), (18, 1, 2)]),
            (30, 3, 295510, [(30, 2, 10), (43, 2, 6)]),
            (1, 0, 495, [(2, 0, 20)]),
            (1, 1, 18048, [(1, 0, 15)]),
            (1, 2, 236946, [(1, 1, 20)]),
            (31, 0, 19671, [(44, 0, 20)]),
            (31, 1, 82572, [(31, 0, 12)]),
            (31, 2, 459362, [(31, 1, 15)]),
            (32, 0, 262, [(45, 0, 20)]),
            (32, 1, 18048, [(32, 0, 20)]),
            (32, 2, 183125, [(31, 1, 5), (32, 1, 10)]),
            (33, 0, 774, [(46, 0, 20)]),
            (33, 1, 22587, [(33, 0, 20)]),
            (33, 2, 203548, [(33, 1, 25)]),
            (34, 0, 5196, [(47, 0, 20)]),
            (34, 1, 64770, [(34, 0, 15)]),
            (34, 2, 250022, [(34, 1, 20)]),
            (39, 0, 50259, [(48, 0, 20)]),
            (39, 1, 246536, [(39, 0, 10)]),
            (39, 2, 731674, [(39, 1, 12), (34, 2, 2)]),
            (36, 0, 7252, [(49, 0, 20)]),
            (36, 1, 25118, [(36, 0, 15)]),
            (36, 2, 228278, [(36, 1, 20)]),
            (37, 0, 5196, [(50, 0, 20)]),
            (37, 1, 29988, [(37, 0, 15)]),
            (37, 2, 303837, [(37, 1, 20)]),
            (38, 0, 10507, [(51, 0, 20)]),
            (38, 1, 63177, [(38, 0, 12)]),
            (38, 2, 325027, [(38, 1, 15)]),
            (40, 0, 36603, [(52, 0, 20)]),
            (40, 1, 185026, [(40, 0, 10), (39, 0, 2)]),
            (40, 2, 774986, [(40, 1, 20), (39, 2, 2)]),
            (17, 1, 3982, [(17, 0, 9)]),
            (17, 2, 31373, [(17, 1, 11)]),
            (18, 1, 7867, [(18, 0, 12)]),
            (18, 2, 53149, [(18, 1, 14)]),
            (43, 1, 3753, [(43, 0, 10)]),
            (43, 2, 37794, [(43, 1, 12)]),
        ];

        [TestMethod]
        public void Crafting_xp_thresholds_match_EggSharp() {
            CollectionAssert.AreEqual(XpThresholds, ArtifactCrafting.CraftingLevelXpThresholds.ToArray());
        }

        [TestMethod]
        public void Crafting_level_multipliers_match_EggSharp() {
            CollectionAssert.AreEqual(RarityMultipliers, Enumerable.Range(0, RarityMultipliers.Length).Select(i => ArtifactCrafting.CraftingLevelMultipliers[i]).ToArray());
            Assert.HasCount(RarityMultipliers.Length, ArtifactCrafting.CraftingLevelMultipliers);
        }

        [TestMethod]
        public void Crafting_level_follows_thresholds() {
            Assert.AreEqual(1u, ArtifactHelpers.GetCraftingLevel(0));
            Assert.AreEqual(1u, ArtifactHelpers.GetCraftingLevel(499));
            Assert.AreEqual(2u, ArtifactHelpers.GetCraftingLevel(500));
            Assert.AreEqual(30u, ArtifactHelpers.GetCraftingLevel(5070943000));
        }

        [TestMethod]
        public void Base_crafting_coefficients_match_EggSharp() {
            var actual = ArtifactCrafting.BaseCraftingCoefficients;
            Assert.HasCount(Coefficients.Length, actual);
            foreach(var (afxId, tier, rare, epic, legendary) in Coefficients) {
                var key = new EggIncArtifactInstance { Id = (byte)afxId, Tier = (byte)tier };
                Assert.IsTrue(actual.TryGetValue(key, out var rates), $"missing {key.Artifact} T{tier}");
                CollectionAssert.AreEqual(new List<double> { rare, epic, legendary }, rates, $"{key.Artifact} T{tier}");
            }
        }

        [TestMethod]
        public void Stone_slots_match_EggSharp_stoneCap() {
            foreach(var (afxId, tier, rarity, slots) in StoneSlots) {
                var instance = new EggIncArtifactInstance { Id = (byte)afxId, Tier = (byte)tier, Rarity = (byte)rarity };
                Assert.AreEqual(slots, EggIncArtifacts.SlotCount(instance), $"{instance.Artifact} T{tier} R{rarity}");
            }
        }

        [TestMethod]
        public void Stone_slots_are_zero_for_stones() {
            Assert.AreEqual(0, EggIncArtifacts.SlotCount(new EggIncArtifactInstance { Id = 1, Tier = 2, Rarity = 1 }));
        }

        [TestMethod]
        public void Recipes_and_initial_prices_match_EggSharp() {
            foreach(var (afxId, level, initial, recipe) in Recipes) {
                var tier = ArtifactFamilies.Tier(afxId, level);
                Assert.IsNotNull(tier, $"{afxId}-{level}");
                Assert.AreEqual<double?>(initial, tier.InitialCraftingPrice, $"{afxId}-{level} price");
                CollectionAssert.AreEqual(recipe, tier.Recipe.Select(i => (i.AfxId, i.Level, i.Count)).ToArray(), $"{afxId}-{level} recipe");
            }
            Assert.AreEqual(Recipes.Length, ArtifactFamilies.All.SelectMany(f => f.Tiers).Count(t => t.Craftable));
        }

        [TestMethod]
        public void Crafting_cost_uses_EggSharp_price() {
            var basket = new Crafter([]).GetCraft(1, 2, "lunar-totem");
            Assert.AreEqual(553u, basket.GetTotalCost());
            Assert.AreEqual(3u, basket.GetIngredients()["0-0"].Need);
        }

        [TestMethod]
        [DataRow("puzzle-cube", "Puzzle cube", 23, new[] { 23 }, 4)]
        [DataRow("lunar-totem", "Lunar totem", 0, new[] { 0 }, 4)]
        [DataRow("gusset", "Gusset", 8, new[] { 8 }, 4)]
        [DataRow("vial-of-martian-dust", "Vial of Martian dust", 7, new[] { 7 }, 4)]
        [DataRow("tachyon-stone", "Tachyon stone", 1, new[] { 1, 2 }, 4)]
        [DataRow("clarity-stone", "Clarity stone", 40, new[] { 40, 52 }, 4)]
        [DataRow("tau-ceti-geode", "Tau Ceti geode", 18, new[] { 18 }, 3)]
        public void Families_keep_EGG9000_ids_and_shape(string id, string name, int afxId, int[] children, int tiers) {
            var family = ArtifactFamilies.Find(id);
            Assert.IsNotNull(family);
            Assert.AreEqual(name, family.Name);
            Assert.AreEqual(afxId, family.AfxId);
            CollectionAssert.AreEqual(children, family.ChildAfxIds.ToArray());
            Assert.HasCount(tiers, family.Tiers);
            Assert.HasCount(34, ArtifactFamilies.All);
        }

        [TestMethod]
        [DataRow(23, 0, 1, "Ancient puzzle cube")]
        [DataRow(6, 3, 4, "Beggspoke Demeters necklace")]
        [DataRow(30, 3, 4, "Meggnificent Mercury's lens")]
        [DataRow(2, 0, 1, "Tachyon stone fragment")]
        [DataRow(1, 0, 2, "Tachyon stone")]
        [DataRow(1, 2, 4, "Brilliant tachyon stone")]
        [DataRow(18, 0, 1, "Tau Ceti geode piece")]
        [DataRow(5, 3, 4, "Brilliant light of Eggendil")]
        public void Tier_names_and_numbers_match(int afxId, int level, int tierNumber, string name) {
            var tier = ArtifactFamilies.Tier(afxId, level);
            Assert.AreEqual(tierNumber, tier.TierNumber);
            Assert.AreEqual(name, tier.Name);
        }

        [TestMethod]
        [DataRow(23, 4, 4, "-60%", "research cost")]
        [DataRow(0, 4, 4, "200x", "away earnings")]
        [DataRow(0, 2, 2, "8x", "away earnings")]
        [DataRow(7, 4, 4, "+500", "max running chicken bonus")]
        [DataRow(4, 4, 4, "Guaranteed", "chance of gold in gifts and drones")]
        [DataRow(4, 4, 2, "+1000%", "chance of gold in gifts and drones")]
        [DataRow(22, 4, 3, "+900%", "chance of cash in gifts and drones")]
        [DataRow(11, 4, 4, "+1400%", "Soul Egg collection rate")]
        [DataRow(5, 4, 4, "250x", "enlightenment egg value")]
        [DataRow(5, 3, 1, "+900%", "enlightenment egg value")]
        [DataRow(10, 4, 4, "+1.2%", "Egg of Prophecy bonus")]
        [DataRow(29, 4, 4, "+15", "hold to hatch rate")]
        [DataRow(26, 4, 4, "+20%", "co-op teammates' egg laying rate")]
        public void Artifact_effect_display_matches(int afxId, int tier, int rarity, string size, string target) {
            var display = EggIncArtifacts.GetEffectDisplay(new EggIncArtifactInstance { Id = (byte)afxId, Tier = (byte)tier, Rarity = (byte)rarity });
            Assert.AreEqual<(string, string)?>((size, target), display);
        }

        [TestMethod]
        [DataRow(10, 0L, 114405L, 360472L, 934701L)]
        [DataRow(0, 0L, 553L, 12234L, 91819L)]
        [DataRow(33, 0L, 774L, 22587L, 203548L)]
        [DataRow(8, 0L, 5167L, 28986L, 163774L)]
        public void Fairness_uses_eggsharp_crafting_prices(int afxId, long t1, long t2, long t3, long t4) {
            CollectionAssert.AreEqual(new[] { t1, t2, t3, t4 }, ArtifactHelpers.GetFairness(new EggIncArtifactInstance { Id = (byte)afxId, Tier = 1 }));
        }

        [TestMethod]
        [DataRow(40, 1, "25%", "effect of host artifact on enlightenment egg farms")]
        [DataRow(39, 3, "+0.15%", "Egg of Prophecy bonus")]
        [DataRow(37, 3, "+100", "max running chicken bonus")]
        [DataRow(34, 3, "+25%", "bonus per Soul Egg")]
        [DataRow(1, 1, "+2%", "egg laying rate")]
        public void Stone_effect_display_matches(int afxId, int tier, string size, string target) {
            var display = EggIncArtifacts.GetEffectDisplay(new EggIncArtifactInstance { Id = (byte)afxId, Tier = (byte)tier, Rarity = 1 });
            Assert.AreEqual<(string, string)?>((size, target), display);
        }
    }
}
