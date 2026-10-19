using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class GameDataParityTests {
        private static readonly Lazy<JsonElement> Fixture = new(() =>
            JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameDataParity.json"))).RootElement);

        private static readonly Dictionary<string, int> ResearchListLevelChanges = new() {
            ["motivational_clucking"] = 50,
        };

        private static readonly Dictionary<string, decimal> ResearchListIncreaseChanges = new() {
            ["cheaper_research"] = -0.05M,
        };

        private static readonly Dictionary<string, int> ResearchJsonLevelChanges = new() {
            ["hold_to_research"] = 20,
            ["afx_mission_time"] = 60,
        };

        private static readonly Dictionary<string, string> EpicDescriptionChanges = new() {
            ["epic_multiplier"] = "Increase max running chicken bonus by 2.0×!",
        };

        private static readonly Dictionary<int, double> EggValueChanges = new() {
            [50] = 1,
            [51] = 1,
            [52] = 1,
            [53] = 1,
            [54] = 1,
        };

        private static readonly Dictionary<string, (int Minutes, int Value)> BoostChanges = new() {
            ["quantum_bulb"] = (10, 1),
            ["jimbos_blue_v2"] = (20, 3),
            ["jimbos_blue_big"] = (240, 3),
            ["jimbos_purple_v2"] = (15, 10),
            ["jimbos_purple_big"] = (180, 10),
            ["tachyon_prism_blue_v2"] = (10, 10),
            ["tachyon_prism_purple_v2"] = (10, 100),
            ["soul_beacon_blue_v2"] = (30, 5),
            ["soul_beacon_purple_v2"] = (20, 50),
        };

        private static readonly Dictionary<int, long[]> FairnessChanges = new() {
            [0] = [0, 553, 12234, 91819],
            [33] = [0, 774, 22587, 203548],
        };

        private static readonly Dictionary<(string, int), uint> CraftCostChanges = new() {
            [("lunar-totem", 2)] = 553,
            [("lunar-totem", 3)] = 14580,
            [("lunar-totem", 4)] = 157870,
            [("lunar-stone", 2)] = 774,
            [("lunar-stone", 3)] = 31584,
            [("lunar-stone", 4)] = 592739,
        };

        private const int LunarTotem = 0;

        private static IEnumerable<JsonElement> Rows(string section) => Fixture.Value.GetProperty(section).EnumerateArray();

        private static string? NullableString(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();

        private static void AssertNone(List<string> mismatches) =>
            Assert.IsEmpty(mismatches, $"{mismatches.Count} mismatches:{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}");

        private static bool Close(double expected, double actual, double relative) =>
            expected == actual || Math.Abs(expected - actual) <= relative * Math.Abs(expected);

        [TestMethod]
        public void Research_list_levels_increase_and_bucket_match_master() {
            var all = Research.EpicResearchList.Concat(Research.CommonResearchList).ToList();
            var mismatches = new List<string>();
            var rows = Rows("researchList").ToList();
            foreach(var r in rows) {
                var id = r[0].GetString()!;
                var levels = ResearchListLevelChanges.GetValueOrDefault(id, r[2].GetInt32());
                var increase = ResearchListIncreaseChanges.GetValueOrDefault(id, (decimal)r[3].GetDouble());
                var item = all.SingleOrDefault(x => x.id == id);
                if(item is null) {
                    mismatches.Add($"{id}: missing");
                    continue;
                }
                if(item.Levels != levels) mismatches.Add($"{id}.Levels: {levels} != {item.Levels}");
                if(item.Increase != increase) mismatches.Add($"{id}.Increase: {increase} != {item.Increase}");
                if(item.Type.ToString() != r[4].GetString()) mismatches.Add($"{id}.Type: {r[4].GetString()} != {item.Type}");
                if(item.Epic != r[5].GetBoolean()) mismatches.Add($"{id}.Epic: {r[5].GetBoolean()} != {item.Epic}");
            }
            Assert.HasCount(rows.Count, all);
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Research_list_names_match_master() {
            var all = Research.EpicResearchList.Concat(Research.CommonResearchList).ToList();
            var mismatches = Rows("researchList")
                .Select(r => (Id: r[0].GetString(), Name: r[1].GetString()))
                .Where(r => all.Single(x => x.id == r.Id).Name != r.Name)
                .Select(r => $"{r.Id}: \"{r.Name}\" != \"{all.Single(x => x.id == r.Id).Name}\"")
                .ToList();
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Common_research_cost_multiplier_matches_master() {
            for(uint level = 0; level <= 10; level++) {
                var epic = new List<CustomResearch> {
                    new() { Id = "cheaper_research", Level = level }
                };
                Assert.AreEqual(1 - 0.05 * level, Research.GetCommonResearchCostMultiplier(null, epic), 1e-12, $"level {level}");
            }
        }

        [TestMethod]
        public void Research_json_levels_per_level_and_tier_match_master() {
            var data = EiResearch.Get();
            var mismatches = new List<string>();
            var rows = Rows("research").ToList();
            foreach(var r in rows) {
                var id = r[0].GetString()!;
                var item = data.SingleOrDefault(x => x.Id == id);
                if(item is null) {
                    mismatches.Add($"{id}: missing");
                    continue;
                }
                var levels = ResearchJsonLevelChanges.GetValueOrDefault(id, r[1].GetInt32());
                if(item.Levels != levels) mismatches.Add($"{id}.Levels: {levels} != {item.Levels}");
                if(!Close(r[2].GetDouble(), item.PerLevel, 1e-12)) mismatches.Add($"{id}.PerLevel: {r[2].GetDouble()} != {item.PerLevel}");
                if(item.Tier != r[3].GetInt32()) mismatches.Add($"{id}.Tier: {r[3].GetInt32()} != {item.Tier}");
            }
            Assert.HasCount(rows.Count, data);
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Research_json_common_prices_match_master_within_rounding() {
            AssertNone(PriceMismatches(true));
        }

        [TestMethod]
        public void Research_json_epic_prices_match_master() {
            AssertNone(PriceMismatches(false));
        }

        private static List<string> PriceMismatches(bool common) {
            var data = EiResearch.Get();
            var mismatches = new List<string>();
            foreach(var r in Rows("research")) {
                var id = r[0].GetString()!;
                var item = data.Single(x => x.Id == id);
                if((item.Type == "common") != common) continue;
                var prices = item.Prices ?? [];
                var count = ResearchJsonLevelChanges.GetValueOrDefault(id, r[4].GetInt32());
                if(prices.Count != count) mismatches.Add($"{id}.Prices.Count: {count} != {prices.Count}");
                if(prices.Count == 0) continue;
                if(!Close(r[5].GetDouble(), prices[0], 0.01)) mismatches.Add($"{id}.Prices[0]: {r[5].GetDouble()} != {prices[0]}");
                if(!Close(r[6].GetDouble(), prices[^1], 0.01)) mismatches.Add($"{id}.Prices[^1]: {r[6].GetDouble()} != {prices[^1]}");
            }
            return mismatches;
        }

        [TestMethod]
        public void Epic_research_costs_and_titles_match_master() {
            var items = EiEpicResearch.Get().epicResearchItems;
            var mismatches = new List<string>();
            foreach(var r in Rows("epicResearch").Where(r => r[4].ValueKind != JsonValueKind.Null)) {
                var id = r[0].GetString()!;
                var item = items.SingleOrDefault(x => x.id == id);
                if(item is null) {
                    mismatches.Add($"{id}: missing");
                    continue;
                }
                var title = NullableString(r[4]);
                var expectedTitle = id.StartsWith("nter_", StringComparison.Ordinal) ? title : title?.ToUpperInvariant();
                if(item._firstCost != r[1].GetInt32()) mismatches.Add($"{id}._firstCost: {r[1].GetInt32()} != {item._firstCost}");
                if(item._lastCost != r[2].GetInt32()) mismatches.Add($"{id}._lastCost: {r[2].GetInt32()} != {item._lastCost}");
                if(item._numLevels != r[3].GetInt32()) mismatches.Add($"{id}._numLevels: {r[3].GetInt32()} != {item._numLevels}");
                if(item.title != expectedTitle) mismatches.Add($"{id}.title: \"{expectedTitle}\" != \"{item.title}\"");
                var overrides = r[6].EnumerateArray().Select(x => x.GetInt32()).ToList();
                if(!overrides.SequenceEqual(item._overrideCosts ?? [])) mismatches.Add($"{id}._overrideCosts differ");
            }
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Epic_research_descriptions_match_master() {
            var items = EiEpicResearch.Get().epicResearchItems;
            var mismatches = new List<string>();
            foreach(var r in Rows("epicResearch")) {
                var id = r[0].GetString()!;
                var item = items.SingleOrDefault(x => x.id == id);
                if(item is null) continue;
                var expected = EpicDescriptionChanges.GetValueOrDefault(id, NullableString(r[5]));
                if(item.description != expected) mismatches.Add($"{id}: \"{expected}\" != \"{item.description}\"");
            }
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Egg_values_match_master() {
            var eggs = Root.Get().eggIncEggs;
            var mismatches = new List<string>();
            var rows = Rows("eggs").ToList();
            foreach(var r in rows) {
                var id = r[0].GetInt32();
                var expected = EggValueChanges.GetValueOrDefault(id, r[1].GetDouble());
                var egg = eggs.SingleOrDefault(e => e.id == id);
                if(egg is null) mismatches.Add($"{id}: missing");
                else if(egg.value != expected) mismatches.Add($"{id}: {expected} != {egg.value}");
            }
            Assert.HasCount(rows.Count, eggs);
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Boost_durations_and_values_match_master() {
            var boosts = Root.Get().eggIncBoosts;
            var mismatches = new List<string>();
            var rows = Rows("boosts").ToList();
            foreach(var r in rows) {
                var id = r[0].GetString()!;
                var (minutes, value) = BoostChanges.GetValueOrDefault(id, (r[1].GetInt32(), r[2].GetInt32()));
                var boost = boosts.SingleOrDefault(b => b.id == id);
                if(boost is null) {
                    mismatches.Add($"{id}: missing");
                    continue;
                }
                if(boost.timeMinutesInt != minutes) mismatches.Add($"{id}.timeMinutesInt: {minutes} != {boost.timeMinutesInt}");
                if(boost.value != value) mismatches.Add($"{id}.value: {value} != {boost.value}");
            }
            Assert.HasCount(rows.Count, boosts);
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Boost_costs_match_master() {
            var boosts = Root.Get().eggIncBoosts;
            var mismatches = new List<string>();
            foreach(var r in Rows("boosts")) {
                var id = r[0].GetString();
                var boost = boosts.Single(b => b.id == id);
                if(boost.costGoldenEggs != r[3].GetInt32()) mismatches.Add($"{id}.costGoldenEggs: {r[3].GetInt32()} != {boost.costGoldenEggs}");
                if(boost.costTokens != r[4].GetInt32()) mismatches.Add($"{id}.costTokens: {r[4].GetInt32()} != {boost.costTokens}");
            }
            AssertNone(mismatches);
        }

        private static double LunarTotemValueFromDisplay(string size) =>
            size.EndsWith('x')
                ? double.Parse(size[..^1], CultureInfo.InvariantCulture)
                : 1 + double.Parse(size.TrimStart('+').TrimEnd('%'), CultureInfo.InvariantCulture) / 100;

        private static Ei.ArtifactSpec Spec(int name, int level, int rarity) => new() {
            Name = (Ei.ArtifactSpec.Types.Name)name,
            Level = (Ei.ArtifactSpec.Types.Level)level,
            Rarity = (Ei.ArtifactSpec.Types.Rarity)rarity
        };

        [TestMethod]
        public void Artifact_values_match_master() {
            var mismatches = new List<string>();
            foreach(var r in Rows("artifactEffects")) {
                var (id, level, rarity) = (r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32());
                var expected = id == LunarTotem ? LunarTotemValueFromDisplay(r[4].GetString()!) : r[3].GetDouble();
                var actual = EggIncArtifacts.GetArtifact(Spec(id, level, rarity)).Value;
                if((float)expected != actual) mismatches.Add($"{(ArtifactNames)id} L{level} R{rarity}: {expected} != {actual}");
            }
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Artifact_effect_display_matches_master() {
            var targets = Rows("artifactTargets").ToDictionary(r => r[0].GetInt32(), r => r[1].GetString()!);
            var mismatches = new List<string>();
            foreach(var r in Rows("artifactEffects")) {
                var (id, level, rarity) = (r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32());
                var instance = new EggIncArtifactInstance { Id = (byte)id, Tier = (byte)(level + 1), Rarity = (byte)(rarity + 1) };
                var expected = ((string, string)?)(r[4].GetString()!, targets[id]);
                var actual = EggIncArtifacts.GetEffectDisplay(instance);
                if(!Equals(actual, expected)) mismatches.Add($"{(ArtifactNames)id} L{level} R{rarity}: {expected} != {actual}");
            }
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Artifacts_without_master_effect_rows_stay_neutral() {
            var listed = Rows("artifactEffects").Select(r => (r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32())).ToHashSet();
            var additive = EggIncArtifacts.GetArtifactsDB.Where(a => a.Additive).Select(a => (int)a.Name).ToHashSet();
            var mismatches = new List<string>();
            for(var id = 0; id <= 52; id++) {
                for(var level = 0; level < 4; level++) {
                    for(var rarity = 0; rarity < 4; rarity++) {
                        if(listed.Contains((id, level, rarity))) continue;
                        var expected = additive.Contains(id) ? 0f : 1f;
                        var value = EggIncArtifacts.GetArtifact(Spec(id, level, rarity)).Value;
                        var display = EggIncArtifacts.GetEffectDisplay(new EggIncArtifactInstance { Id = (byte)id, Tier = (byte)(level + 1), Rarity = (byte)(rarity + 1) });
                        if(value != expected || display is not null) mismatches.Add($"{(ArtifactNames)id} L{level} R{rarity}: {value} {display}");
                    }
                }
            }
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Fairness_matches_master() {
            var mismatches = new List<string>();
            var rows = Rows("fairness").ToList();
            foreach(var r in rows) {
                var id = r[0].GetInt32();
                var expected = FairnessChanges.GetValueOrDefault(id, new[] { r[1].GetInt64(), r[2].GetInt64(), r[3].GetInt64(), r[4].GetInt64() });
                var actual = ArtifactHelpers.GetFairness(new EggIncArtifactInstance { Id = (byte)id, Tier = 1 });
                if(!expected.SequenceEqual(actual)) mismatches.Add($"{(ArtifactNames)id}: [{string.Join(", ", expected)}] != [{string.Join(", ", actual)}]");
            }
            Assert.HasCount(53, rows);
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Craft_baskets_match_master() {
            var mismatches = new List<string>();
            foreach(var r in Rows("crafts")) {
                var (slug, tier) = (r[0].GetString()!, r[1].GetInt32());
                if(r[2].ValueKind == JsonValueKind.Null) {
                    Assert.ThrowsExactly<ArgumentException>(() => new Crafter([]).GetCraft(1, tier, slug), $"{slug} T{tier}");
                    continue;
                }
                var cost = CraftCostChanges.GetValueOrDefault((slug, tier), r[2].GetUInt32());
                var needs = r[3].EnumerateObject().ToDictionary(p => p.Name, p => (uint)p.Value.GetInt32());
                var basket = new Crafter([]).GetCraft(1, tier, slug);
                if(basket.GetTotalCost() != cost) mismatches.Add($"{slug} T{tier} cost: {cost} != {basket.GetTotalCost()}");
                var actualNeeds = basket.GetIngredients().Where(kv => kv.Value.Need > 0).ToDictionary(kv => kv.Key, kv => kv.Value.Need);
                if(needs.Count != actualNeeds.Count || needs.Any(kv => actualNeeds.GetValueOrDefault(kv.Key) != kv.Value)) {
                    mismatches.Add($"{slug} T{tier} needs: {string.Join(",", needs.OrderBy(k => k.Key))} != {string.Join(",", actualNeeds.OrderBy(k => k.Key))}");
                }
            }
            AssertNone(mismatches);
        }

        [TestMethod]
        public void Removed_epic_research_rows_stay_local() {
            var ids = EiEpicResearch.Get().epicResearchItems.Select(x => x.id).ToList();
            CollectionAssert.Contains(ids, "epic_silo_quality");
            CollectionAssert.Contains(ids, "warp_shift");
        }
    }
}
