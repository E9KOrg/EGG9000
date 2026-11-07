using EggSharp.GameData;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public static class ArtifactCrafting {
        public const int MaxCraftingLevel = 30;

        private static readonly Lazy<Dictionary<EggIncArtifactInstance, List<double>>> _coefficients = new(() => BuildCoefficients(GameDataSource.Artifacts.Artifacts));
        private static readonly Lazy<Dictionary<int, double>> _multipliers = new(() => BuildMultipliers(GameDataSource.Artifacts.CraftingLevels));
        private static readonly Lazy<List<long>> _thresholds = new(() => BuildThresholds(GameDataSource.Artifacts.CraftingLevels));

        public static IReadOnlyDictionary<EggIncArtifactInstance, List<double>> BaseCraftingCoefficients => _coefficients.Value;

        public static IReadOnlyDictionary<int, double> CraftingLevelMultipliers => _multipliers.Value;

        public static IReadOnlyList<long> CraftingLevelXpThresholds => _thresholds.Value;

        public static Dictionary<EggIncArtifactInstance, List<double>> BuildCoefficients(IEnumerable<ArtifactCatalogEntry> artifacts) =>
            artifacts
                .GroupBy(a => (a.SpecName, a.Level))
                .Where(g => g.Count() > 1)
                .ToDictionary(
                    g => new EggIncArtifactInstance {
                        Tier = (byte)(g.First().AfxLevel + 1),
                        Id = (byte)g.First().AfxId
                    },
                    g => {
                        var common = g.First(a => a.Rarity == "COMMON").OddsMultiplier;
                        return new List<double> {
                            Coefficient(g, common, "RARE"),
                            Coefficient(g, common, "EPIC"),
                            Coefficient(g, common, "LEGENDARY")
                        };
                    });

        public static Dictionary<int, double> BuildMultipliers(IReadOnlyList<ArtifactCraftingLevelEntry> levels) =>
            levels.Select((level, index) => (index, level.RarityMult)).ToDictionary(x => x.index, x => x.RarityMult);

        public static List<long> BuildThresholds(IReadOnlyList<ArtifactCraftingLevelEntry> levels) {
            long runningSum = 0;
            return [.. levels.Select(level => runningSum += (long)level.XpRequired).Prepend(0).Take(MaxCraftingLevel)];
        }

        private static double Coefficient(IEnumerable<ArtifactCatalogEntry> group, double common, string rarity) {
            var ratio = common / (group.FirstOrDefault(a => a.Rarity == rarity)?.OddsMultiplier ?? 1);
            return ratio == common ? 0 : Math.Round(ratio, 0, MidpointRounding.AwayFromZero);
        }
    }
}
