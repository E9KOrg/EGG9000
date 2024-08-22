using EGG9000.Common.Helpers;
using Ei;
using Humanizer;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using static Ei.MissionInfo.Types;

namespace EGG9000.Common.JsonData.EiAfxConfig {


    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class ArtifactParameter {
        public Spec spec { get; set; }
        public double baseQuality { get; set; }
        public double oddsMultiplier { get; set; }
        public double value { get; set; }
        public double craftingPrice { get; set; }
        public double craftingPriceLow { get; set; }
        public int craftingPriceDomain { get; set; }
        public double craftingPriceCurve { get; set; }
    }

    public class Duration {
        public string durationType { get; set; }
        public int seconds { get; set; }
        public double quality { get; set; }
        public double minQuality { get; set; }
        public double maxQuality { get; set; }
        public int capacity { get; set; }
        public int levelCapacityBump { get; set; }
        public double levelQualityBump { get; set; }

        public DurationType durationTypeEnum {
            get {
                if(Enum.TryParse<DurationType>(durationType, ignoreCase: true, out var parsedEnum)) return parsedEnum;

                return DurationType.Tutorial;
            }
        }
    }

    public class MissionParameter {
        public string ship { get; set; }
        public List<Duration> durations { get; set; }
        public List<int> levelMissionRequirements { get; set; }
        public int capacityDEPRECATED { get; set; }

        public Spaceship shipEnum { 
            get {
                if(Enum.TryParse<Spaceship>(ship, ignoreCase: true, out var parsedEnum)) return parsedEnum;

                if(Enum.TryParse<Spaceship>(ship.Replace("_", ""), ignoreCase: true, out var parsedEnum2)) return parsedEnum2;

                return Spaceship.ChickenOne;
            }
        }
    }

    public class CraftingLevelInfo {
        public int xpRequired { get; set; }
        public double rarityMult { get; set; }
    }

    public class Root {
        public List<MissionParameter> missionParameters { get; set; }
        public List<ArtifactParameter> artifactParameters { get; set; }
        public List<CraftingLevelInfo> craftingLevelInfos { get; set; }
        public Dictionary<EggIncArtifactInstance, List<double>> baseCraftingCoefficients { get; set; }
        public Dictionary<int, double> craftingLevelMultipliers { get; set; }
        public List<long> craftingLevelXpThresholds { get; set; }

        private static Root Instance = null;
        public static Root Get() {
            if(Instance != null) {
                return Instance;
            }

            var assembly = Assembly.GetExecutingAssembly();

            var resourceName = assembly.GetManifestResourceNames()
                .Single(str => str.EndsWith("eiafx-config.json"));

            using var stream = assembly.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            Instance = JsonConvert.DeserializeObject<Root>(json);

            // Compile the crafting coefficients from artifactParameters
            Instance.baseCraftingCoefficients = Instance.artifactParameters
                .GroupBy(config => new { config.spec.name, config.spec.level })
                .Where(artifactLevelGrouping => artifactLevelGrouping.Count() > 1) // Weed out stones and ingredients, artifact levels with no rarity
                .ToDictionary(
                    artifactLevelGrouping => {
                        // Create an EggIncArtifactInstance
                        var firstGroup = artifactLevelGrouping.First();
                        var afInstanceName = firstGroup.spec.name.Replace("_", " ").Titleize();
                        afInstanceName = afInstanceName.Replace("Vial ", "Vial Of ");
                        afInstanceName = afInstanceName.Replace("Of", "of").Replace(" In ", " in ").Replace(" A ", " a ");
                        afInstanceName = afInstanceName.Replace("Ornate ", "");
                        afInstanceName = afInstanceName.Replace("Mercurys ", "Mercury's ");

                        var afInstance = new EggIncArtifactInstance() {
                            Tier = (byte)((int)Enum.Parse<ArtifactSpec.Types.Level>(firstGroup.spec.level, ignoreCase: true) + 1),
                        };

                        return afInstance;
                    },
                    artifactLevelGrouping => {
                        var commonCoeff = artifactLevelGrouping.First(c => c.spec.rarity == "COMMON").oddsMultiplier;
                        var rareCoeff = commonCoeff / (artifactLevelGrouping.FirstOrDefault(c => c.spec.rarity == "RARE")?.oddsMultiplier ?? 1);
                        var epicCoeff = commonCoeff / (artifactLevelGrouping.FirstOrDefault(c => c.spec.rarity == "EPIC")?.oddsMultiplier ?? 1);
                        var legendaryCoeff = commonCoeff / (artifactLevelGrouping.FirstOrDefault(c => c.spec.rarity == "LEGENDARY")?.oddsMultiplier ?? 1);

                        // Ensure coefficients are set to 0 if they are undefined
                        if(rareCoeff == commonCoeff) rareCoeff = 0;
                        if(epicCoeff == commonCoeff) epicCoeff = 0;
                        if(legendaryCoeff == commonCoeff) legendaryCoeff = 0;

                        rareCoeff = Math.Round(rareCoeff, 0, MidpointRounding.AwayFromZero);
                        epicCoeff = Math.Round(epicCoeff, 0, MidpointRounding.AwayFromZero);
                        legendaryCoeff = Math.Round(legendaryCoeff, 0, MidpointRounding.AwayFromZero);

                        return new List<double> { rareCoeff, epicCoeff, legendaryCoeff };
                    }
                );

            Instance.craftingLevelMultipliers = Instance.craftingLevelInfos.ToDictionary(info => Instance.craftingLevelInfos.IndexOf(info), info => info.rarityMult);

            long runningSum = 0;
            Instance.craftingLevelXpThresholds = Instance.craftingLevelInfos
                .Select(level => {
                    runningSum += level.xpRequired;
                    return runningSum;
                })
                .Prepend(0).Take(30).ToList();

            return Instance;
        }
    }

    public class Spec {
        public string name { get; set; }
        public string level { get; set; }
        public string rarity { get; set; }
        public string egg { get; set; }
    }

}
