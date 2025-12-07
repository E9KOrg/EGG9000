using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiStatics;
using EGG9000.Common.Migrations;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using static EGG9000.Common.Coops.ArtifactCombos;

namespace EGG9000.Common.Coops {
    public class ArtifactCombos {
        public static List<EggIncArtifactInstance> FindBestCombo(CustomBackup backup, CustomFarm farm, Coop coop, bool withTachyon, bool allowChangingStones, List<DBCustomEgg> customEggs, Microsoft.Extensions.Logging.ILogger logger) {
            if(!allowChangingStones) {
                var available = backup.GetAvailableArtifacts(farm).Where(x => !x.Artifact.Artifact.Contains("Stone")).Select(x => new ArtifactInstanceStats(x.Artifact)).Where(x => x.Shipping > 1 || x.EggLaying > 1);


                return Process(available, backup, farm, coop, customEggs, withTachyon);
            } else {
                var timings = new TimingsFactory(logger).Start();

                var available = backup.GetAvailableArtifacts(farm).Where(x => !x.Artifact.Artifact.Contains("Stone")).Select(x => new ArtifactCountWithSlots(x, EggIncArtifacts.SlotCount(x.Artifact)));

                var allStoneCombos = new List<ArtifactInstanceStats>();

                var stones = backup.ArtifactHall
                    .Where(x => x.Artifact.Artifact.Contains("Stone") && (x.Artifact.Boost == EggIncBoostTypeEnum.EggShippingRate || x.Artifact.Boost == EggIncBoostTypeEnum.EggLayingRate));

                var possibleStones = stones.Select(x => x.Artifact).ToList();


                var toProcess = available.Where(x => x.Artifact.Boost == EggIncBoostTypeEnum.EggShippingRate || x.Artifact.Boost == EggIncBoostTypeEnum.EggLayingRate || x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates).ToList();
                
                
                var nonBoostArtifacts = available.Where(x => x.Artifact.Boost != EggIncBoostTypeEnum.EggShippingRate && x.Artifact.Boost != EggIncBoostTypeEnum.EggLayingRate && x.Artifact.Boost != EggIncBoostTypeEnum.CoopMembersEggLayingRates).OrderByDescending(x => x.Slots).ToList();
                var currentNonBoostArtifacts = farm.Artifacts.Where(x => x.Boost != EggIncBoostTypeEnum.EggShippingRate && x.Boost != EggIncBoostTypeEnum.EggLayingRate && x.Boost != EggIncBoostTypeEnum.CoopMembersEggLayingRates).Select(x => new ArtifactCountWithSlots(x, EggIncArtifacts.SlotCount(x))).ToList();



                for(var i = 0; i < 3; i++) {
                    var maxSlot = currentNonBoostArtifacts.MaxBy(x => x.Slots);
                    if(maxSlot is not null && maxSlot.Slots >= (nonBoostArtifacts.FirstOrDefault()?.Slots ?? 0)) {
                        currentNonBoostArtifacts.Remove(maxSlot);
                    } else if(nonBoostArtifacts.Count > 0) {
                        maxSlot = nonBoostArtifacts.First();
                        nonBoostArtifacts.Remove(maxSlot);
                    }
                    toProcess.Add(maxSlot);
                }
                
                if(toProcess.All(x => x is null)) {
                    return new List<EggIncArtifactInstance>();
                }

                foreach(var artifactCount in toProcess.GroupBy(x => new {x.Artifact.Rarity, x.Artifact.Tier, x.Artifact }).Select(x => x.First())) {
                    var slots = artifactCount.Slots;
                    var artifact = artifactCount.Artifact;
                    var combos = FillStones(slots, possibleStones);
                    foreach(var combo in combos) {
                        allStoneCombos.Add(new ArtifactInstanceStats(new EggIncArtifactInstance { Additive = artifact.Additive, Boost = artifact.Boost, Rarity = artifact.Rarity, Stones = combo, Tier = artifact.Tier, Value = artifact.Value, Id = artifact.Id }));
                    }

                }

                timings.Set("All Combos Generated");

                var list = Process(allStoneCombos, backup, farm, coop, customEggs, withTachyon);
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
            public ArtifactCountWithSlots(EggIncArtifactInstance a, int slots) {
                Slots = slots;
                Artifact = a;
                Count = 1;
            }
        }

        private static List<List<EggIncArtifactInstance>> FillStones(int slots, List<EggIncArtifactInstance> possibleStones) {
            if(slots == 0) return [[]];

            var stonesCombos = FillStones(slots - 1, possibleStones);
            var newCombos = new List<List<EggIncArtifactInstance>>();
            foreach(var stoneCombo in stonesCombos) {
                foreach(var stone in possibleStones) {
                    newCombos.Add(new List<EggIncArtifactInstance>(stoneCombo) {
                        stone
                    });
                }
            }
            return newCombos;
        }

        private static List<EggIncArtifactInstance> Process(IEnumerable<ArtifactInstanceStats> available, CustomBackup backup, CustomFarm farm, Coop coop, List<DBCustomEgg> customEggs, bool withTachyon) {
            var farmWithoutArtifacts = new CustomFarm {
                CommonResearch = farm.CommonResearch,
                Habs = farm.Habs, TrainLength = farm.TrainLength, NumChickens = farm.NumChickens, EggType = farm.EggType, Artifacts = new List<EggIncArtifactInstance>(), Vehicles = farm.Vehicles
            };
            var statsWithoutArtifacts = farmWithoutArtifacts.WithStats(backup, coop, customEggs, (farm.Artifacts.FirstOrDefault(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)?.Value ?? 1) - 1, coop.Contract);

            var currentSet = new ArtifactSet(
                [
                    new(farm.Artifacts.FirstOrDefault()),
                    new(farm.Artifacts.Skip(1).FirstOrDefault()),
                    new(farm.Artifacts.Skip(2).FirstOrDefault()),
                    new(farm.Artifacts.Skip(3).FirstOrDefault())
                ], statsWithoutArtifacts
            );

            var keepArtifacts = new List<EggIncArtifactInstance>();
            if(farm.Artifacts.Any(x => x.Boost == EggIncBoostTypeEnum.HabCapacity)) {
                keepArtifacts.Add(farm.Artifacts.First(x => x.Boost == EggIncBoostTypeEnum.HabCapacity));
            }
            //if(farm.Artifacts.Any(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates) && withTachyon) {
            //    keepArtifacts.Add(farm.Artifacts.First(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates));
            //} else if(withTachyon) {
            //    var tachyon = available.Where(x => x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates).MaxBy(x => x.Artifact.Value);
            //    if(tachyon is not null)
            //        keepArtifacts.Add(tachyon.Artifact);
            //}

            var artifacts = available
                .Where(x => !keepArtifacts.Any(y => y.Artifact == x.Artifact.Artifact))
                .GroupBy(x => new { x.Shipping, x.EggLaying, isTach = x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates })
                .Select(x =>
                    x.OrderBy(y => farm.Artifacts.Any(z => z.Equals(y.Artifact)) ? 0 : 1
                ).First())
                .ToList();

            var sets = new List<ArtifactSet>();

            for(var i = 0; i < artifacts.Count; i++) {
                for(var j = i + 1; j < artifacts.Count; j++) {
                    if(artifacts[i].Artifact.Id == artifacts[j].Artifact.Id)
                        continue;
                    if(keepArtifacts.Count == 2) {
                        var set = new ArtifactSet(new List<ArtifactInstanceStats> { new ArtifactInstanceStats(keepArtifacts[0]), new ArtifactInstanceStats(keepArtifacts[1]), artifacts[i], artifacts[j] }, statsWithoutArtifacts);
                        if(CheckSet(set, withTachyon)) 
                            sets.Add(set);
                        continue;
                    }
                    for(var k = j + 1; k < artifacts.Count; k++) {
                        if(artifacts[i].Artifact.Id == artifacts[k].Artifact.Id ||
                            artifacts[j].Artifact.Id == artifacts[k].Artifact.Id)
                            continue;
                        if(keepArtifacts.Count == 1) {
                            var set = new ArtifactSet(new List<ArtifactInstanceStats> { new ArtifactInstanceStats(keepArtifacts[0]), artifacts[i], artifacts[j], artifacts[k] }, statsWithoutArtifacts);
                            if(CheckSet(set, withTachyon)) 
                                sets.Add(set);
                            continue;
                        }
                        for(var l = k + 1; l < artifacts.Count; l++) {
                            if(artifacts[i].Artifact.Id == artifacts[l].Artifact.Id ||
                                artifacts[j].Artifact.Id == artifacts[l].Artifact.Id ||
                                artifacts[k].Artifact.Id == artifacts[l].Artifact.Id
                                )
                                continue;
                            var set = new ArtifactSet(new List<ArtifactInstanceStats> { artifacts[i], artifacts[j], artifacts[k], artifacts[l] }, statsWithoutArtifacts);
                            if(CheckSet(set, withTachyon)) 
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

            return maxSetsScored.OrderByDescending(s => s.Score).FirstOrDefault()?.ArtiList;
        }

        private static int SimilarityScoring(ArtifactSet current, ArtifactSet against) {
            var similarity = 0;

            var currentSet = current.Artifacts;
            var againstSet = against.Artifacts;
            // check for equivalent artifacts in both sets
            foreach(var artifact in currentSet) {
                var matchingArtifact = againstSet.FirstOrDefault(x => x.Artifact.Equals(artifact.Artifact));

                if(matchingArtifact != null) {
                    similarity++;
                    // test for similarity between shipping and egg laying between artifact sets
                    if(artifact.Shipping == matchingArtifact.Shipping) {
                        similarity += Math.Max(artifact.Artifact.Stones.Count, 1);
                    } else similarity--;
                    if(artifact.EggLaying == matchingArtifact.EggLaying) {
                        similarity += Math.Max(artifact.Artifact.Stones.Count, 1);
                    } else similarity--;
                    if(artifact.Shipping != 0 && artifact.EggLaying != 0) {
                        if(artifact.Shipping == matchingArtifact.Shipping && artifact.EggLaying == matchingArtifact.EggLaying) {
                            similarity += Math.Max(artifact.Artifact.Stones.Count, 1);
                        } else similarity--;
                    }
                } else {
                    //If a matching artifact cannot be found, very hurtful
                    similarity -= 2;
                }
            }
            return similarity;
        }

        public static bool CheckSet(ArtifactSet set, bool withTachyon) {
            return !withTachyon || set.Artifacts.Any(x => x.Artifact.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates);
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
