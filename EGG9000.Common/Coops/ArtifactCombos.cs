using EGG9000.Bot;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Coops {
    public class ArtifactCombos {
        public static List<EggIncArtifactInstance> FindBestCombo(CustomBackup backup, CustomFarm farm, Coop coop, bool withTachyon, bool allowChangingStones, Microsoft.Extensions.Logging.ILogger logger) {
            if(!allowChangingStones) {
                var available = backup.GetAvailableArtifacts(farm).Where(x => !x.Artifact.Artifact.Contains("Stone")).Select(x => new ArtifactInstanceStats(x.Artifact)).Where(x => x.Shipping > 1 || x.EggLaying > 1);


                return Process(available, backup, farm, coop, withTachyon);
            } else {
                var available = backup.GetAvailableArtifacts(farm).Where(x => !x.Artifact.Artifact.Contains("Stone"));

                var allCombos = new List<ArtifactInstanceStats>();

                foreach(var artifact in available.Where(x => x.Artifact.Boost == EggIncBoostTypeEnum.EggShippingRate).GroupBy(x => new {x.Artifact.Rarity, x.Artifact.Tier }).Select(x => x.First())) {
                    var slots = EggIncArtifacts.SlotCount(artifact.Artifact);
                } 

                return Process(allCombos, backup, farm, coop, withTachyon);

            }
        }


        private static List<EggIncArtifactInstance> Process(IEnumerable<ArtifactInstanceStats> available, CustomBackup backup, CustomFarm farm, Coop coop, bool withTachyon) {
            var farmWithoutArtifacts = new CustomFarm {
                CommonResearch = farm.CommonResearch,
                Habs = farm.Habs, TrainLength = farm.TrainLength, NumChickens = farm.NumChickens, EggType = farm.EggType, Artifacts = new List<EggIncArtifactInstance>(), Vehicles = farm.Vehicles
            };
            var statsWithoutArtifacts = farmWithoutArtifacts.WithStats(backup, coop, (farm.Artifacts.FirstOrDefault(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)?.Value ?? 1) - 1);

            var keepArtifacts = new List<EggIncArtifactInstance>();
            if(farm.Artifacts.Any(x => x.Boost == EggIncBoostTypeEnum.HabCapacity)) {
                keepArtifacts.Add(farm.Artifacts.First(x => x.Boost == EggIncBoostTypeEnum.HabCapacity));
            }
            if(farm.Artifacts.Any(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates) && withTachyon) {
                keepArtifacts.Add(farm.Artifacts.First(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates));
            } else if(withTachyon) {
                var tachyon = backup.GetAvailableArtifacts(farm).Where(x => x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates).MaxBy(x => x.Artifact.Value);
                if(tachyon is not null)
                    keepArtifacts.Add(tachyon.Artifact);
            }

            var artifacts = available
                .GroupBy(x => new { x.Shipping, x.EggLaying })
                .Select(x => 
                    x.OrderBy(y => farm.Artifacts.Any(z => z.Equals(y.Artifact)) ? 0 : 1
                ).First())
                .ToList();


            var sets = new List<ArtifactSet>();

            for(var i = 0; i < artifacts.Count; i++) {
                for(var j = i + 1; j < artifacts.Count; j++) {
                    if(artifacts[i].Artifact.Artifact == artifacts[j].Artifact.Artifact)
                        continue;
                    if(keepArtifacts.Count == 2) {
                        var set = new ArtifactSet(new List<ArtifactInstanceStats> { new ArtifactInstanceStats(keepArtifacts[0]), new ArtifactInstanceStats(keepArtifacts[1]), artifacts[i], artifacts[j] }, statsWithoutArtifacts);
                        sets.Add(set);
                        continue;
                    }
                    for(var k = j + 1; k < artifacts.Count; k++) {
                        if(artifacts[i].Artifact.Artifact == artifacts[k].Artifact.Artifact ||
                            artifacts[j].Artifact.Artifact == artifacts[k].Artifact.Artifact)
                            continue;
                        if(keepArtifacts.Count == 1) {
                            var set = new ArtifactSet(new List<ArtifactInstanceStats> { new ArtifactInstanceStats(keepArtifacts[0]), artifacts[i], artifacts[j], artifacts[k] }, statsWithoutArtifacts);
                            sets.Add(set);
                            continue;
                        }
                        for(var l = k + 1; l < artifacts.Count; l++) {
                            if(artifacts[i].Artifact.Artifact == artifacts[l].Artifact.Artifact ||
                                artifacts[j].Artifact.Artifact == artifacts[l].Artifact.Artifact ||
                                artifacts[k].Artifact.Artifact == artifacts[l].Artifact.Artifact
                                )
                                continue;
                            var set = new ArtifactSet(new List<ArtifactInstanceStats> { artifacts[i], artifacts[j], artifacts[k], artifacts[l] }, statsWithoutArtifacts);
                            sets.Add(set);
                        }
                    }
                }
            }

            var order = sets.OrderByDescending(x => x.CurrentShippingRate).ToList();
            var max = sets.Max(x => x.CurrentShippingRate);
            var maxSets = sets.Where(x => x.CurrentShippingRate == max).ToList();
            return maxSets.Select(x => x.Artifacts.Select(y => y.Artifact).ToList()).First();
        }

        public class ArtifactSet {
            public List<ArtifactInstanceStats> Artifacts;
            public double Shipping;
            public double EggLaying;
            public double CurrentShippingRate;

            public ArtifactSet(List<ArtifactInstanceStats> artifacts, CustomFarmStats stats) {
                Artifacts = artifacts;
                Shipping = artifacts.Select(x => x.Shipping).Aggregate((x, y) => x * y) * stats.MaxShippingRate;
                EggLaying = artifacts.Select(x => x.EggLaying).Aggregate((x, y) => x * y) * stats.EggLayingRate;
                CurrentShippingRate = Math.Min(Shipping, EggLaying);
            }
        }

        public class ArtifactInstanceStats {
            public EggIncArtifactInstance Artifact { get; set; }
            public double Shipping { get; set; }
            public double EggLaying { get; set; }
            //public string ShippingPlusEgg { get; set; }

            public ArtifactInstanceStats(EggIncArtifactInstance instance) {
                Artifact = instance;
                Shipping = EggIncArtifacts.GetMultiple(EggIncBoostTypeEnum.EggShippingRate, new List<EggIncArtifactInstance> { instance }, false);
                EggLaying = EggIncArtifacts.GetMultiple(EggIncBoostTypeEnum.EggLayingRate, new List<EggIncArtifactInstance> { instance }, false);
                //ShippingPlusEgg = $"{Shipping}{EggLaying}";
            }
        }
    }
}
