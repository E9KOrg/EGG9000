using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using EGG9000.Common.Database;
using EGG9000.Common.JsonData.EiAfxData;
using MessagePack;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using Newtonsoft.Json;

namespace EGG9000.Common.Helpers {
    public class EggIncArtifacts {
        public static double GetMultiple(EggIncBoostTypeEnum boostType, CustomFarm farm) {
            var enlightenment = farm.EggType == Ei.Egg.Enlightenment;
            return GetMultiple(boostType, farm.Artifacts, enlightenment);
        }

        public static double GetMultiple(EggIncBoostTypeEnum boostType, List<EggIncArtifactInstance> artifacts, bool enlightenment) {
            var rate = 1.0;

            artifacts.ForEach(x => {
                if(x.Stones == null)
                    x.Stones = new List<EggIncArtifactInstance>();
                double farmMultiple = (enlightenment && x.Boost != EggIncBoostTypeEnum.EnlightenmentEggValue) ? 0 : 1;
                farmMultiple += x.Stones.Where(s => s.Boost == EggIncBoostTypeEnum.HostArtifactsOnElightenment).Sum(s => s.Value);
                if(x.Boost == boostType) {
                    rate *= GetEnlightenmentRate(x, farmMultiple);
                }

                foreach(var stone in x.Stones.Where(x => x.Boost == boostType)) {
                    rate *= GetEnlightenmentRate(stone, farmMultiple); //stone.Value * (x.Boost == EggIncBoostTypeEnum.EnlightenmentEggValue ? 1 : farmMutiple);
                }
            });

            return rate;
        }

        private static double GetEnlightenmentRate(EggIncArtifactInstance artifact, double enlightenmentMultiple) {
            var rate = artifact.Value - 1;
            rate *= enlightenmentMultiple;
            return rate + 1;
        }

        public static double GetEggLayingRateMultiple(CustomFarm farm) {
            return GetMultiple(EggIncBoostTypeEnum.EggLayingRate, farm);
        }

        public static double GetShippingMultiple(CustomFarm farm) {
            return GetMultiple(EggIncBoostTypeEnum.EggShippingRate, farm);
            //return (artifacts.Where(x => x.Boost == EggIncBoostTypeEnum.EggShippingRate).Sum(x => (double?)x.Value - 1) ?? 0) + 1;
        }

        public static double GetEggValueMutiple(CustomFarm farm) {
            return GetMultiple(EggIncBoostTypeEnum.EggValue, farm);
            //return (artifacts.Where(x => x.Boost == EggIncBoostTypeEnum.EggValue).Sum(x => (double?)x.Value - 1) ?? 0) + 1;
        }

        private static EiAfxDataRoot _eiAfxDataRoot;

        public static EiAfxDataRoot GetEiAfxData() {
            if(_eiAfxDataRoot == null) {
                var assembly = Assembly.GetExecutingAssembly();

                string resourceName = assembly.GetManifestResourceNames()
                    .Single(str => str.EndsWith("eiafx-data.json"));

                using(Stream stream = assembly.GetManifestResourceStream(resourceName))
                using(StreamReader reader = new StreamReader(stream)) {
                    string json = reader.ReadToEnd();
                    _eiAfxDataRoot = JsonConvert.DeserializeObject<EiAfxDataRoot>(json);
                }
            }

            return _eiAfxDataRoot;
        }

        public static Tier GetTier(int afxId, int tierNumber) {
            var data = GetEiAfxData();
            // Should be do so, because stone fragment tiers have a different afx_id.
            var artifact = data.artifact_families.FirstOrDefault(x => x.tiers.Any(y => y.afx_id == afxId));
            if(artifact is null) {
                throw new Exception("Unable to locate artifact family with afx_id: " + afxId);
            }

            var tier = artifact.tiers.FirstOrDefault(x => x.tier_number == tierNumber);
            if(tier is null) {
                throw new Exception($"Unable to locate tier {tierNumber} for {artifact.name}");
            }

            return tier;
        }

        public static int SlotCount(EggIncArtifactInstance instance) {
            var data = GetEiAfxData();
            var artifact = data.artifact_families.FirstOrDefault(x => x.name.Equals(instance.Artifact, StringComparison.OrdinalIgnoreCase));
            if(artifact is null) {
                throw new Exception("Unable to locate artifact family: " + instance.Artifact);
            }

            var tier = artifact.tiers.FirstOrDefault(x => x.tier_number == instance.Tier);
            if(tier is null) {
                throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}");
            }

            if(!tier.has_rarities)
                return 0;
            var rarity = tier.effects.FirstOrDefault(x => x.afx_rarity == instance.Rarity - 1);
            if(rarity is null) {
                throw new Exception($"Unable to locate rarity {instance.Rarity} for {instance.Artifact} with tier {instance.Tier}");
            }

            return rarity.slots ?? 0;
        }

        public static string GetProperNameFromJson(EggIncArtifactInstance instance) {
            var data = GetEiAfxData();
            var artifact = data.artifact_families.FirstOrDefault(x => x.name.Equals(instance.Artifact, StringComparison.OrdinalIgnoreCase));
            if(artifact is null) {
                throw new Exception("Unable to locate artifact family: " + instance.Artifact);
            }

            var tier = artifact.tiers.FirstOrDefault(x => x.tier_number == instance.Tier);
            if(tier is null) {
                throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}");
            }

            return tier.name;
        }
        
        public static string GetTierName(EggIncArtifactInstance instance) {
            var data = GetEiAfxData();
            var isFragment = instance.Artifact.Contains("Fragment", StringComparison.OrdinalIgnoreCase);
            ArtifactFamily artifact;
            if(instance.Artifact.Contains("Fragment", StringComparison.OrdinalIgnoreCase)) {
                artifact = data.artifact_families.FirstOrDefault(x => x.name.Equals(instance.Artifact.Replace("Fragment", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd(), StringComparison.OrdinalIgnoreCase));
            } else {
                artifact = data.artifact_families.FirstOrDefault(x => x.name.Equals(instance.Artifact, StringComparison.OrdinalIgnoreCase));
            }

            if(artifact is null) {
                throw new Exception("Unable to locate artifact family: " + instance.Artifact);
            }

            var tier = artifact.tiers.FirstOrDefault(x => x.tier_number ==  instance.Tier + (artifact.type.Equals("Stone", StringComparison.OrdinalIgnoreCase) && !isFragment ? 1: 0));
            if(tier is null) {
                throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}");
            }

            return tier.name;
        }

        public static string GetNameFromJson(EggIncArtifactInstance instance) {
            var data = GetEiAfxData();
            var artifact = data.artifact_families.FirstOrDefault(x => x.name.Equals(instance.Artifact, StringComparison.OrdinalIgnoreCase));
            if(artifact is null) {
                throw new Exception("Unable to locate artifact family: " + instance.Artifact);
            }

            return artifact.id;
        }


        public static double GetMaxRunningBonusAdditive(CustomFarm farm) {
            double val = 0;
            foreach(var artifact in farm.Artifacts) {
                if(artifact.Boost == EggIncBoostTypeEnum.MaxRunningChickenBonus) {
                    val += artifact.Value;
                }

                foreach(var stone in artifact.Stones) {
                    if(stone.Boost == EggIncBoostTypeEnum.MaxRunningChickenBonus) {
                        val += stone.Value;
                    }
                }
            }

            return val;
            //return (artifacts.Where(x => x.Boost == EggIncBoostTypeEnum.MaxRunningChickenBonus).Sum(x => (double?)x.Value) ?? 0);
        }


        public static double GetHabSpaceMultiple(CustomFarm farm) {
            return GetMultiple(EggIncBoostTypeEnum.HabCapacity, farm);
        }

        public static EggIncArtifactInstance GetArtifact(Ei.ArtifactSpec artifactSpec) {
            if(artifactSpec == null) {
                return null;
            }

            var artifact = GetArtifactsDB.FirstOrDefault(x => x.Name == (int)artifactSpec.Name);
            if(artifact == null)
                return null;
            var response = new EggIncArtifactInstance {
                Additive = artifact.Additive,
                Artifact = artifact.Artifact,
                Boost = artifact.Boost,
                Tier = (byte)(artifactSpec.Level + 1),
                Rarity = (byte)(artifactSpec.Rarity + 1),
                //Spec = artifactSpec
            };
            switch((int)artifactSpec.Level) {
                case 0:
                    response.Value = artifact.L0R0;
                    break;
                case 1:
                    switch((int)artifactSpec.Rarity) {
                        case 0:
                            response.Value = artifact.L1R0;
                            break;
                        case 1:
                            response.Value = artifact.L1R1;
                            break;
                        case 2:
                            response.Value = artifact.L1R2;
                            break;
                    }

                    break;
                case 2:
                    switch((int)artifactSpec.Rarity) {
                        case 0:
                            response.Value = artifact.L2R0;
                            break;
                        case 1:
                            response.Value = artifact.L2R1;
                            break;
                        case 2:
                            response.Value = artifact.L2R2;
                            break;
                    }

                    break;
                case 3:
                    switch((int)artifactSpec.Rarity) {
                        case 0:
                            response.Value = artifact.L3R0;
                            break;
                        case 1:
                            response.Value = artifact.L3R1;
                            break;
                        case 2:
                            response.Value = artifact.L3R2;
                            break;
                        case 3:
                            response.Value = artifact.L3R3;
                            break;
                    }

                    break;
            }

            if(response.Value == 0 && !response.Additive) {
                response.Value = 1;
            }

            return response;
        }

        public static List<EggIncArtifact> GetArtifactsDB = new List<EggIncArtifact> {
            new EggIncArtifact {
                Name = 21, Artifact = "Aurelian Brooch", Boost = EggIncBoostTypeEnum.DroneRewards, //done
                L0R0 = 1.1,
                L1R0 = 1.25,
                L2R0 = 1.5, L2R1 = 1.6, L2R2 = 1.7,
                L3R0 = 2, L3R1 = 2.25, L3R2 = 2.5, L3R3 = 3
            },
            new EggIncArtifact {
                Name = 4, Artifact = "Beak of Midas", Boost = EggIncBoostTypeEnum.DroneRewards, //done
                L0R0 = 1.2,
                L1R0 = 1.5,
                L2R0 = 2, L2R1 = 3,
                L3R0 = 6, L3R1 = 11, L3R3 = 9999
            },
            new EggIncArtifact {
                Name = 10, Artifact = "Book of Basan", Boost = EggIncBoostTypeEnum.EggsOfProphecyEffect, //done
                L0R0 = 1.0025,
                L1R0 = 1.005,
                L2R0 = 1.0075, L2R2 = 1.008,
                L3R0 = 1.01, L3R2 = 1.1, L3R3 = 1.2
            },
            new EggIncArtifact {
                Name = 22, Artifact = "Carved Rainstick", Boost = EggIncBoostTypeEnum.CashGiftChance, //done
                L0R0 = 1.2,
                L1R0 = 1.5,
                L2R2 = 2,
                L3R0 = 5, L3R2 = 10, L3R3 = 9999
            },
            new EggIncArtifact {
                Name = 40, Artifact = "Clarity Stone", Boost = EggIncBoostTypeEnum.HostArtifactsOnElightenment, //done
                L0R0 = 0.25,
                L1R0 = 0.50,
                L2R0 = 1
            },
            new EggIncArtifact {
                Name = 6, Artifact = "Demeters Necklace", Boost = EggIncBoostTypeEnum.EggValue, //done
                L0R0 = 1.1,
                L1R0 = 1.25, L1R1 = 1.35,
                L2R0 = 1.5, L2R1 = 1.6, L2R2 = 1.75,
                L3R0 = 2, L3R1 = 2.25, L3R2 = 2.5, L3R3 = 3
            },
            new EggIncArtifact {
                Name = 28, Artifact = "Dilithium Monocle", Boost = EggIncBoostTypeEnum.BoostEffectiveness, //done
                L0R0 = 1.05,
                L1R0 = 1.1,
                L2R0 = 1.14,
                L3R0 = 1.2, L3R2 = 1.25, L3R3 = 1.3
            },
            new EggIncArtifact {
                Name = 31, Artifact = "Dilithium Stone", Boost = EggIncBoostTypeEnum.BoostDuration, //done
                L0R0 = 1.03,
                L1R0 = 1.06,
                L2R0 = 1.08
            },
            new EggIncArtifact {
                Name = 8, Artifact = "Gusset", Boost = EggIncBoostTypeEnum.HabCapacity, //done
                L0R0 = 1.05,
                L1R0 = 1.1, L1R2 = 1.12,
                L2R0 = 1.14, L2R1 = 1.15,
                L3R0 = 1.2, L3R2 = 1.22, L3R3 = 1.25
            },
            new EggIncArtifact {
                Name = 27, Artifact = "Interstellar Compass", Boost = EggIncBoostTypeEnum.EggShippingRate, //done
                L0R0 = 1.05,
                L1R0 = 1.1,
                L2R0 = 1.2, L2R1 = 1.22,
                L3R0 = 1.3, L3R1 = 1.35, L3R2 = 1.4, L3R3 = 1.5
            },
            new EggIncArtifact {
                Name = 38, Artifact = "Life Stone", Boost = EggIncBoostTypeEnum.InternalHatchery, //done
                L0R0 = 1.02,
                L1R0 = 1.03,
                L2R0 = 1.04
            },
            new EggIncArtifact {
                Name = 5, Artifact = "Light of Eggendil", Boost = EggIncBoostTypeEnum.EnlightenmentEggValue, //done
                L0R0 = 1.5,
                L1R0 = 2, L1R1 = 2.2,
                L2R0 = 10, L2R1 = 15,
                L3R0 = 101, L3R2 = 151, L3R3 = 251
            },
            new EggIncArtifact {
                Name = 33, Artifact = "Lunar Stone", Boost = EggIncBoostTypeEnum.AwayEarnings, //done
                L0R0 = 1.2,
                L1R0 = 1.3,
                L2R0 = 1.4
            },
            new EggIncArtifact {
                Name = 0, Artifact = "Lunar Totem", Boost = EggIncBoostTypeEnum.AwayEarnings, //done
                L0R0 = 1.5,
                L1R0 = 2, L1R1 = 2.5,
                L2R0 = 4, L2R1 = 5,
                L3R0 = 6, L3R1 = 8, L3R2 = 10
            },
            new EggIncArtifact {
                Name = 30, Artifact = "Mercury's Lens", Boost = EggIncBoostTypeEnum.FarmValue, //done
                L0R0 = 1.1,
                L1R0 = 1.2, L1R1 = 1.22,
                L2R0 = 1.5, L2R1 = 1.55,
                L3R0 = 2, L3R1 = 2.25, L3R2 = 2.5, L3R3 = 3
            },
            new EggIncArtifact {
                Name = 3, Artifact = "Neodymium Medallion", Boost = EggIncBoostTypeEnum.DroneFrequency, //done
                L0R0 = 1.1,
                L1R0 = 1.25, L1R1 = 1.3,
                L2R0 = 1.5, L2R2 = 1.6,
                L3R0 = 2, L3R1 = 2.1, L3R2 = 2.2, L3R3 = 2.29
            },
            new EggIncArtifact {
                Name = 11, Artifact = "Phoenix Feather", Boost = EggIncBoostTypeEnum.SoulEggCollectionRate, //done
                L0R0 = 1.25,
                L1R0 = 2,
                L2R0 = 5, L2R1 = 6,
                L3R0 = 10,
                L3R1 = 12,
                L3R3 = 15
            },
            new EggIncArtifact {
                Name = 39, Artifact = "Prophecy Stone", Boost = EggIncBoostTypeEnum.EggsOfProphecyEffect, //done
                L0R0 = 1.0005,
                L1R0 = 1.001,
                L2R0 = 1.0015
            },
            new EggIncArtifact {
                Name = 23, Artifact = "Puzzle Cube", Boost = EggIncBoostTypeEnum.ResearchCost, //done
                L0R0 = 1.05,
                L1R0 = 1.1, L1R2 = 1.15,
                L2R0 = 1.2, L2R1 = 1.22,
                L3R0 = 1.5, L3R1 = 1.53, L3R2 = 1.55, L3R3 = 1.6
            },
            new EggIncArtifact {
                Name = 24, Artifact = "Quantum Metronome", Boost = EggIncBoostTypeEnum.EggLayingRate, //done
                L0R0 = 1.05,
                L1R0 = 1.1, L1R1 = 1.12,
                L2R0 = 1.14999, L2R1 = 1.17, L2R2 = 1.2,
                L3R0 = 1.25, L3R1 = 1.27, L3R2 = 1.3, L3R3 = 1.35
            },
            new EggIncArtifact {
                Name = 36, Artifact = "Quantum Stone", Boost = EggIncBoostTypeEnum.EggShippingRate, //done
                L0R0 = 1.02,
                L1R0 = 1.04,
                L2R0 = 1.05
            },
            new EggIncArtifact {
                Name = 32, Artifact = "Shell Stone", Boost = EggIncBoostTypeEnum.EggValue, //done
                L0R0 = 1.05,
                L1R0 = 1.08,
                L2R0 = 1.10
            },
            new EggIncArtifact {
                Name = 25, Artifact = "Ship in a Bottle", Boost = EggIncBoostTypeEnum.CoopMembersEarnings, //done
                L0R0 = 1.2,
                L1R0 = 1.3,
                L2R0 = 1.5, L2R1 = 1.6,
                L3R0 = 1.7, L3R1 = 1.8, L3R2 = 1.9, L3R3 = 2
            },
            new EggIncArtifact {
                Name = 34, Artifact = "Soul Stone", Boost = EggIncBoostTypeEnum.SoulEggBonus, //done
                L0R0 = 1.05,
                L1R0 = 1.1,
                L2R0 = 1.25
            },
            new EggIncArtifact {
                Name = 26, Artifact = "Tachyon Deflector", Boost = EggIncBoostTypeEnum.CoopMembersEggLayingRates, //done
                L0R0 = 1.05,
                L1R0 = 1.08,
                L2R0 = 1.12, L2R1 = 1.12,
                L3R0 = 1.14, L3R1 = 1.17, L3R2 = 1.19, L3R3 = 1.2
            },
            new EggIncArtifact {
                Name = 1, Artifact = "Tachyon Stone", Boost = EggIncBoostTypeEnum.EggLayingRate, //done
                L0R0 = 1.02,
                L1R0 = 1.04,
                L2R0 = 1.05
            },
            new EggIncArtifact {
                Name = 37, Artifact = "Terra Stone", Boost = EggIncBoostTypeEnum.RunningChickenBonus, Additive = true, //done
                L0R0 = 10,
                L1R0 = 50,
                L2R0 = 100
            },
            new EggIncArtifact {
                Name = 9, Artifact = "The Chalice", Boost = EggIncBoostTypeEnum.InternalHatchery, //done
                L0R0 = 1.05,
                L1R0 = 1.1, L1R2 = 1.14,
                L2R0 = 1.2, L2R1 = 1.23, L2R2 = 1.25,
                L3R0 = 1.3, L3R2 = 1.35, L3R3 = 1.4
            },
            new EggIncArtifact {
                Name = 29, Artifact = "Titanium Actuator", Boost = EggIncBoostTypeEnum.HoldToHatch, Additive = true, //done
                L0R0 = 1,
                L1R0 = 4,
                L2R0 = 6, L2R1 = 7,
                L3R0 = 10, L3R2 = 12, L3R3 = 15
            },
            new EggIncArtifact {
                Name = 12, Artifact = "Tungsten Ankh", Boost = EggIncBoostTypeEnum.EggValue, //done
                L0R0 = 1.1,
                L1R0 = 1.25, L1R1 = 1.28,
                L2R0 = 1.5, L2R1 = 1.75, L2R3 = 2,
                L3R0 = 2, L3R1 = 2.25, L3R3 = 2.5
            },
            new EggIncArtifact {
                Name = 7, Artifact = "Vial of Martian Dust", Boost = EggIncBoostTypeEnum.MaxRunningChickenBonus, Additive = true, //done
                L0R0 = 10,
                L1R0 = 50, L1R1 = 60,
                L2R0 = 100, L2R2 = 150,
                L3R0 = 200, L3R1 = 300, L3R3 = 500
            },
            new EggIncArtifact { Name = 13, Artifact = "Extraterrestrial Aluminum" },
            new EggIncArtifact { Name = 14, Artifact = "Ancient Tungsten" },
            new EggIncArtifact { Name = 15, Artifact = "Space Rocks" },
            new EggIncArtifact { Name = 16, Artifact = "Alien Wood" },
            new EggIncArtifact { Name = 17, Artifact = "Gold Meteorite" },
            new EggIncArtifact { Name = 18, Artifact = "Tau Ceti Geode" },
            new EggIncArtifact { Name = 19, Artifact = "Centaurian Steel" },
            new EggIncArtifact { Name = 20, Artifact = "Eridant Feather" },
            new EggIncArtifact { Name = 35, Artifact = "Drone Parts" },
            new EggIncArtifact { Name = 41, Artifact = "Celestial Bronze" },
            new EggIncArtifact { Name = 42, Artifact = "Lalande Hide" },
            new EggIncArtifact { Name = 43, Artifact = "Solar Titanium" },
            new EggIncArtifact { Name = 2, Artifact = "Tachyon Stone Fragment" },
            new EggIncArtifact { Name = 44, Artifact = "Dilithium Stone Fragment" },
            new EggIncArtifact { Name = 45, Artifact = "Shell Stone Fragment" },
            new EggIncArtifact { Name = 46, Artifact = "Lunar Stone Fragment" },
            new EggIncArtifact { Name = 47, Artifact = "Soul Stone Fragment" },
            new EggIncArtifact { Name = 48, Artifact = "Prophecy Stone Fragment" },
            new EggIncArtifact { Name = 49, Artifact = "Quantum Stone Fragment" },
            new EggIncArtifact { Name = 50, Artifact = "Terra Stone Fragment" },
            new EggIncArtifact { Name = 51, Artifact = "Life Stone Fragment" },
            new EggIncArtifact { Name = 52, Artifact = "Clarity Stone Fragment" },
        };
    }

    [MessagePackObject]
    public class ArtifactCount {
        [Key(0)] public int Count { get; set; }
        [Key(1)] public EggIncArtifactInstance Artifact { get; set; }
        [Key(2)] public uint NumberCrafted { get; set; }
    }

    [MessagePackObject]
    public class EggIncArtifactInstance : IEquatable<EggIncArtifactInstance> {
        [Key(0)] public string Artifact { get; set; }
        [Key(1)] public EggIncBoostTypeEnum Boost { get; set; }
        [Key(2)] public double Value { get; set; }

        [Key(3)] public bool Additive { get; set; }

        //public Ei.ArtifactSpec Spec { get; set; }
        [Key(4)] public List<EggIncArtifactInstance> Stones { get; set; }
        [Key(5)] public byte Tier { get; set; }
        [Key(6)] public byte Rarity { get; set; }

        public override bool Equals(Object other) {
            if(other is EggIncArtifactInstance)
                return this.Equals((EggIncArtifactInstance)other);
            else
                return false;
        }

        public bool Equals(EggIncArtifactInstance other) {
            if(other == null) {
                return false;
            }

            if(ReferenceEquals(this, other)) {
                return true;
            }

            var stonesAreEqual = (Stones?.Count ?? 0) == (other.Stones?.Count ?? 0);
            if(stonesAreEqual) {
                for(var i = 0; i < (Stones?.Count ?? 0); i++) {
                    if(!other.Stones[i].Equals(Stones[i])) {
                        stonesAreEqual = false;
                        break;
                    }
                }
            }

            var match = Artifact == other.Artifact && Boost == other.Boost && Value == other.Value && Additive == other.Additive && stonesAreEqual;
            return match;
        }

        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 23 + Artifact.GetHashCode();
                hash = hash * 23 + Boost.GetHashCode();
                hash = hash * 23 + Value.GetHashCode();
                hash = hash * 23 + Additive.GetHashCode();
                foreach(var stone in Stones ?? new List<EggIncArtifactInstance>()) {
                    hash = hash * 23 + stone.GetHashCode();
                }

                return hash;
            }
        }
    }

    public class EggIncArtifact {
        public string Artifact { get; set; }
        public int Name { get; set; }
        public EggIncBoostTypeEnum Boost { get; set; }
        public double L0R0 { get; set; }
        public double L1R0 { get; set; }
        public double L1R1 { get; set; }
        public double L1R2 { get; set; }
        public double L2R0 { get; set; }
        public double L2R1 { get; set; }
        public double L2R2 { get; set; }
        public double L2R3 { get; set; }
        public double L3R0 { get; set; }
        public double L3R1 { get; set; }
        public double L3R2 { get; set; }
        public double L3R3 { get; set; }
        public bool Additive { get; set; }
    }
}


/*
    13: "EXTRATERRESTRIAL_ALUMINUM",
    14: "ANCIENT_TUNGSTEN",
    15: "SPACE_ROCKS",
    16: "ALIEN_WOOD",
    17: "GOLD_METEORITE",
    18: "TAU_CETI_GEODE",
    19: "CENTAURIAN_STEEL",
    20: "ERIDANI_FEATHER",
    35: "DRONE_PARTS",
    41: "CELESTIAL_BRONZE",
    42: "LALANDE_HIDE",
    43: "SOLAR_TITANIUM",



    2: "TACHYON_STONE_FRAGMENT",
    45: "SHELL_STONE_FRAGMENT",
    46: "LUNAR_STONE_FRAGMENT",
    47: "SOUL_STONE_FRAGMENT",
    48: "PROPHECY_STONE_FRAGMENT",
    49: "QUANTUM_STONE_FRAGMENT",
    50: "TERRA_STONE_FRAGMENT",
*/