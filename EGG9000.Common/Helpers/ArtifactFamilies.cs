using EggSharp.GameData.Artifacts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public sealed record ArtifactEffectEntry(int Rarity, double Value, bool Guaranteed, int StoneCap, string Size);

    public sealed record ArtifactTierEntry(ArtifactFamilyEntry Family, int AfxId, int Level, int TierNumber, string Name, IReadOnlyList<ArtifactEffectEntry> Effects, IReadOnlyList<ArtifactIngredient> Recipe, double? InitialCraftingPrice) {
        public string Key => $"{AfxId}-{Level}";
        public bool Craftable => Recipe.Count > 0;
    }

    public sealed class ArtifactFamilyEntry(string id, string name, int afxId, ArtifactKind kind, string effectTarget) {
        private readonly List<ArtifactTierEntry> _tiers = [];

        public string Id { get; } = id;
        public string Name { get; } = name;
        public int AfxId { get; } = afxId;
        public string EffectTarget { get; } = effectTarget;
        public bool IsStone => kind == ArtifactKind.Stone;
        public bool IsIngredient => kind == ArtifactKind.Ingredient;
        public bool HasRarities => kind == ArtifactKind.Artifact;
        public IReadOnlyList<ArtifactTierEntry> Tiers => _tiers;
        public IReadOnlyList<int> ChildAfxIds => [AfxId, .. _tiers.Select(t => t.AfxId).Where(x => x != AfxId).Distinct()];

        internal void Add(ArtifactTierEntry tier) => _tiers.Add(tier);
    }

    public static class ArtifactFamilies {
        private const int AwayEarningsDimension = 3;
        private const int SoulEggBonusDimension = 12;
        private const int CountDimension = 20;
        private const int RunningChickenDimension = 24;
        private const int ProphecyBonusDimension = 26;
        private const int EnlightenmentHostDimension = 29;

        private static readonly (int AfxId, string Id, string Name, string Target)[] Table = [
            (23, "puzzle-cube", "Puzzle cube", null),
            (0, "lunar-totem", "Lunar totem", null),
            (6, "demeters-necklace", "Demeters necklace", null),
            (7, "vial-of-martian-dust", "Vial of Martian dust", "max running chicken bonus"),
            (21, "aurelian-brooch", "Aurelian brooch", null),
            (12, "tungsten-ankh", "Tungsten ankh", null),
            (8, "gusset", "Gusset", null),
            (3, "neodymium-medallion", "Neodymium medallion", null),
            (30, "mercurys-lens", "Mercury's lens", null),
            (4, "beak-of-midas", "Beak of Midas", null),
            (22, "carved-rainstick", "Carved rainstick", null),
            (27, "interstellar-compass", "Interstellar compass", null),
            (9, "the-chalice", "The chalice", null),
            (11, "phoenix-feather", "Phoenix feather", null),
            (24, "quantum-metronome", "Quantum metronome", null),
            (28, "dilithium-monocle", "Dilithium monocle", null),
            (29, "titanium-actuator", "Titanium actuator", null),
            (25, "ship-in-a-bottle", "Ship in a bottle", null),
            (26, "tachyon-deflector", "Tachyon deflector", null),
            (10, "book-of-basan", "Book of Basan", "Egg of Prophecy bonus"),
            (5, "light-of-eggendil", "Light of Eggendil", null),
            (33, "lunar-stone", "Lunar stone", null),
            (32, "shell-stone", "Shell stone", null),
            (1, "tachyon-stone", "Tachyon stone", null),
            (37, "terra-stone", "Terra stone", "max running chicken bonus"),
            (34, "soul-stone", "Soul stone", null),
            (31, "dilithium-stone", "Dilithium stone", null),
            (36, "quantum-stone", "Quantum stone", null),
            (38, "life-stone", "Life stone", null),
            (40, "clarity-stone", "Clarity stone", null),
            (39, "prophecy-stone", "Prophecy stone", "Egg of Prophecy bonus"),
            (17, "gold-meteorite", "Gold meteorite", null),
            (18, "tau-ceti-geode", "Tau Ceti geode", null),
            (43, "solar-titanium", "Solar titanium", null),
        ];

        private static readonly Lazy<Snapshot> _snapshot = new(Build);

        public static IReadOnlyList<ArtifactFamilyEntry> All => _snapshot.Value.Families;

        public static ArtifactFamilyEntry Find(string id) => id is null ? null : _snapshot.Value.BySlug.GetValueOrDefault(id);

        public static ArtifactFamilyEntry ForAfxId(int afxId) => _snapshot.Value.ByAfxId.GetValueOrDefault(afxId);

        public static ArtifactFamilyEntry Containing(int afxId) => All.FirstOrDefault(f => f.Tiers.Any(t => t.AfxId == afxId));

        public static ArtifactTierEntry Tier(int afxId, int level) => _snapshot.Value.Tiers.GetValueOrDefault((afxId, level));

        private static Snapshot Build() {
            var caps = StoneCaps();
            var prices = GameDataSource.Artifacts.Artifacts
                .Where(a => a.AfxRarity == 0)
                .GroupBy(a => (a.AfxId, a.AfxLevel))
                .ToDictionary(g => g.Key, g => Math.Floor(g.First().CraftingPrice));
            var families = Table.Select(row => BuildFamily(row, caps, prices)).ToList();
            return new Snapshot(
                families,
                families.ToDictionary(f => f.Id, StringComparer.Ordinal),
                families.ToDictionary(f => f.AfxId),
                families.SelectMany(f => f.Tiers).ToDictionary(t => (t.AfxId, t.Level)));
        }

        private static ArtifactFamilyEntry BuildFamily((int AfxId, string Id, string Name, string Target) row, Dictionary<(string, int, int), int> caps, Dictionary<(int, int), double> prices) {
            var definition = ArtifactDefinitions.Find(row.AfxId) ?? throw new InvalidOperationException($"EggSharp has no artifact family {row.AfxId}");
            var hasEffects = definition.Kind is ArtifactKind.Artifact or ArtifactKind.Stone;
            var target = hasEffects ? (row.Target ?? ArtifactDefinitions.Label(definition.Name))?.Replace("{currency}", "cash") ?? "" : "";
            var family = new ArtifactFamilyEntry(row.Id, row.Name, row.AfxId, definition.Kind, target);

            if(definition.Kind == ArtifactKind.Stone) {
                var fragment = ArtifactDefinitions.All.First(f => f.Kind == ArtifactKind.StoneIngredient && definition.Tiers[0].Recipe.Any(r => r.AfxId == f.AfxId));
                family.Add(BuildTier(family, fragment, fragment.Tiers[0], 1, false, caps, prices));
            }

            var offset = family.Tiers.Count + 1;
            foreach(var tier in definition.Tiers) {
                family.Add(BuildTier(family, definition, tier, tier.Level + offset, hasEffects, caps, prices));
            }
            return family;
        }

        private static ArtifactTierEntry BuildTier(ArtifactFamilyEntry family, ArtifactFamily definition, ArtifactTier tier, int tierNumber, bool hasEffects, Dictionary<(string, int, int), int> caps, Dictionary<(int, int), double> prices) {
            IReadOnlyList<ArtifactEffectEntry> effects = hasEffects
                ? [.. tier.Effects.Select(e => BuildEffect(definition, e, caps.GetValueOrDefault((definition.BinaryId, tier.Level, e.Rarity))))]
                : [];
            double? initial = tier.Recipe.Count > 0 && prices.TryGetValue((definition.AfxId, tier.Level), out var price) ? price : null;
            return new ArtifactTierEntry(family, definition.AfxId, tier.Level, tierNumber, SentenceCase(tier.Name), effects, tier.Recipe, initial);
        }

        private static ArtifactEffectEntry BuildEffect(ArtifactFamily definition, ArtifactEffect effect, int stoneCap) {
            var guaranteed = effect.Magnitude == 0 && effect.Template.Contains("guaranteed", StringComparison.OrdinalIgnoreCase);
            var value = EffectValue(definition, effect);
            return new ArtifactEffectEntry(effect.Rarity, value, guaranteed, stoneCap, SizeText(definition.Dimension, value, guaranteed));
        }

        private static double EffectValue(ArtifactFamily definition, ArtifactEffect effect) =>
            definition.Dimension is SoulEggBonusDimension or ProphecyBonusDimension ? 1 + effect.Magnitude : effect.Magnitude;

        private static string SizeText(int dimension, double value, bool guaranteed) => guaranteed ? "Guaranteed" : dimension switch {
            CountDimension or RunningChickenDimension => $"+{Format(value)}",
            EnlightenmentHostDimension => $"{Format(value * 100)}%",
            AwayEarningsDimension when value >= 5 => $"{Format(value)}x",
            _ when value >= 20 => $"{Format(value)}x",
            _ when value >= 1 => $"+{Format((value - 1) * 100)}%",
            _ => $"{Format((value - 1) * 100)}%"
        };

        private static string Format(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);

        private static readonly Dictionary<string, string> ProperWords = new(StringComparer.Ordinal) {
            ["demeters"] = "Demeters",
            ["mercury's"] = "Mercury's",
            ["aurelian"] = "Aurelian",
            ["martian"] = "Martian",
            ["midas"] = "Midas",
            ["basan"] = "Basan",
            ["eggendil"] = "Eggendil",
            ["tau"] = "Tau",
            ["ceti"] = "Ceti",
        };

        private static string SentenceCase(string name) {
            if(name.Length == 0) return name;
            var text = string.Join(' ', name.ToLowerInvariant().Split(' ').Select(w => ProperWords.GetValueOrDefault(w, w)));
            return char.ToUpperInvariant(text[0]) + text[1..];
        }

        private static Dictionary<(string, int, int), int> StoneCaps() {
            var family = GameDataSource.Provider.Family(EggSharp.GameData.Families.Artifact);
            if(family is null) return [];
            return family.Effects
                .Where(e => e.TryMeta("tier", out _) && e.TryMeta("rarity", out _))
                .ToDictionary(
                    e => (e.MetaString("boost"), e.MetaInt("tier"), e.MetaInt("rarity")),
                    e => e.TryMeta("stoneCap", out var cap) ? Convert.ToInt32(cap) : 0);
        }

        private sealed record Snapshot(
            IReadOnlyList<ArtifactFamilyEntry> Families,
            IReadOnlyDictionary<string, ArtifactFamilyEntry> BySlug,
            IReadOnlyDictionary<int, ArtifactFamilyEntry> ByAfxId,
            IReadOnlyDictionary<(int, int), ArtifactTierEntry> Tiers);
    }
}
