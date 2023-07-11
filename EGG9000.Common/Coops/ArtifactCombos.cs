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
using static EGG9000.Common.Coops.ArtifactCombos;
using static Ei.Backup.Types;

namespace EGG9000.Common.Coops {
    public class ArtifactCombos {
        public static List<EggIncArtifactInstance> FindBestCombo(CustomBackup backup, CustomFarm farm, Coop coop, bool withTachyon, bool allowChangingStones, Microsoft.Extensions.Logging.ILogger logger) {
            if(!allowChangingStones) {
                var available = backup.GetAvailableArtifacts(farm).Where(x => !x.Artifact.Artifact.Contains("Stone")).Select(x => new ArtifactInstanceStats(x.Artifact)).Where(x => x.Shipping > 1 || x.EggLaying > 1);


                return Process(available, backup, farm, coop, withTachyon);
            } else {
                var timings = new TimingsFactory(logger).Start();

                var available = backup.GetAvailableArtifacts(farm).Where(x => !x.Artifact.Artifact.Contains("Stone")).Select(x => new ArtifactCountWithSlots(x, EggIncArtifacts.SlotCount(x.Artifact)));

                var allCombos = new List<ArtifactInstanceStats>();

                var stones = backup.GetAvailableArtifacts(farm)
                    .Where(x => x.Artifact.Artifact.Contains("Stone") && (x.Artifact.Boost == EggIncBoostTypeEnum.EggShippingRate || x.Artifact.Boost == EggIncBoostTypeEnum.EggLayingRate));

                var possibleStones = stones.Select(x => x.Artifact).ToList();


                var toProcess = available.Where(x => x.Artifact.Boost == EggIncBoostTypeEnum.EggShippingRate || x.Artifact.Boost == EggIncBoostTypeEnum.EggLayingRate).ToList();
                available = available.Where(x => x.Artifact.Boost != EggIncBoostTypeEnum.EggShippingRate && x.Artifact.Boost != EggIncBoostTypeEnum.EggLayingRate).ToList();
                var maxSlots1 = available.MaxBy(x => x.Slots);
                toProcess.Add(maxSlots1);
                var maxSlots2 = available.Where(x => x.Artifact.Artifact != maxSlots1.Artifact.Artifact).MaxBy(x => x.Slots);
                toProcess.Add(maxSlots2);

                foreach(var artifactCount in toProcess.GroupBy(x => new {x.Artifact.Rarity, x.Artifact.Tier, x.Artifact }).Select(x => x.First())) {
                    var slots = artifactCount.Slots;
                    var artifact = artifactCount.Artifact;
                    var combos = FillStones(slots, possibleStones);
                    foreach(var combo in combos) {
                        allCombos.Add(new ArtifactInstanceStats(new EggIncArtifactInstance { Additive = artifact.Additive, Artifact = artifact.Artifact, Boost = artifact.Boost, Rarity = artifact.Rarity, Stones = combo, Tier = artifact.Tier, Value = artifact.Value }));
                    }

                }

                timings.Set("All Combos Generated");
                var unique = allCombos.GroupBy(x => x.Artifact.Artifact).Select(x => x.First()).ToList();

                var list = Process(allCombos, backup, farm, coop, withTachyon);
                timings.Finished();
                return list;

            }
        }

        private class ArtifactCountWithSlots : ArtifactCount {
            public int Slots { get; set; }
            public ArtifactCountWithSlots(ArtifactCount a, int slots) {
                Slots = slots;
                Artifact = a.Artifact;
                Count = a.Count;
            }
        }

        private static List<List<EggIncArtifactInstance>> FillStones(int slots, List<EggIncArtifactInstance> possibleStones) {

            if(slots == 0) {
                return new List<List<EggIncArtifactInstance>> { new List<EggIncArtifactInstance>() };
            } 
            var stonesCombos = FillStones(slots - 1, possibleStones);
            var newCombos = new List<List<EggIncArtifactInstance>>();
            foreach(var stoneCombo in stonesCombos) {
                foreach(var stone in possibleStones) {
                    var newCombo = new List<EggIncArtifactInstance>(stoneCombo);
                    newCombo.Add(stone);
                    newCombos.Add(newCombo);
                }
            }
            return newCombos;
        }

        private static List<EggIncArtifactInstance> Process(IEnumerable<ArtifactInstanceStats> available, CustomBackup backup, CustomFarm farm, Coop coop, bool withTachyon) {
            var farmWithoutArtifacts = new CustomFarm {
                CommonResearch = farm.CommonResearch,
                Habs = farm.Habs, TrainLength = farm.TrainLength, NumChickens = farm.NumChickens, EggType = farm.EggType, Artifacts = new List<EggIncArtifactInstance>(), Vehicles = farm.Vehicles
            };
            var statsWithoutArtifacts = farmWithoutArtifacts.WithStats(backup, coop, (farm.Artifacts.FirstOrDefault(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)?.Value ?? 1) - 1);

            var currentSet = new ArtifactSet(
                new List<ArtifactInstanceStats> {
                    new ArtifactInstanceStats(farm.Artifacts[0]),
                    new ArtifactInstanceStats(farm.Artifacts[1]),
                    new ArtifactInstanceStats(farm.Artifacts[2]),
                    new ArtifactInstanceStats(farm.Artifacts[3])
                }, statsWithoutArtifacts
            );

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
                .Where(x => !keepArtifacts.Any(y => y.Artifact == x.Artifact.Artifact))
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

            var order = sets.OrderByDescending(x => x.CurrentShippingRate).ThenByDescending(x => x.Shipping).ThenByDescending(x => x.EggLaying).ToList();
            var max = sets.Any() ? sets.Max(x => x.CurrentShippingRate) : 0;
            var maxSets = sets.Where(x => x.CurrentShippingRate == max).ToList();

            var maxSetsScored = new List<ScoredSet>();
            foreach(var compSet in maxSets) {
                maxSetsScored.Add(new ScoredSet(compSet, SimilarityScoring(currentSet, compSet)));
            }

            return maxSetsScored.OrderByDescending(s => s.Score).FirstOrDefault().ArtiList;
        }

        private static int SimilarityScoring(ArtifactSet current, ArtifactSet against) {
            var similarity = 0;

            var currentSet = current.Artifacts;
            var againstSet = against.Artifacts;

            foreach (var artifact in currentSet) {
                //Try to find the corresponding artifact in the against
                var corr = againstSet.FirstOrDefault(x => x.Artifact.Equals(artifact.Artifact));
                if (corr != null) {
                    similarity++;
                    //If the current has stones, test similarity there
                    if(artifact.Shipping != 0 || artifact.EggLaying != 0) {
                        if(artifact.Shipping == corr.Shipping) similarity++;
                        if(artifact.EggLaying == corr.EggLaying) similarity++;
                    }
                }
            }

            var score = (int)(similarity / (double)Math.Max(currentSet.Count, againstSet.Count) * 100);

            return score;
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

    class ScoredSet {
        public List<EggIncArtifactInstance> ArtiList;
        public ArtifactSet Set;
        public int Score;

        public ScoredSet(ArtifactSet set, int score) { 
            Set = set;
            Score = score;
            ArtiList = new List<EggIncArtifactInstance> {
                Set.Artifacts[0].Artifact,
                Set.Artifacts[1].Artifact,
                Set.Artifacts[2].Artifact,
                Set.Artifacts[3].Artifact
            };
        }
    }
}
