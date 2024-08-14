using EGG9000.Common.Database;
using EGG9000.Common.JsonData.EiAfxData;
using EGG9000.Common.JsonData.EiStatics;
using MessagePack;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;

namespace EGG9000.Common.Helpers {
    public class EggIncArtifacts {
        public static double GetMultiple(EggIncBoostTypeEnum boostType, CustomFarm farm) {
            var enlightenment = farm.EggType == Ei.Egg.Enlightenment;
            return GetMultiple(boostType, farm.Artifacts, enlightenment);
        }

        public static double GetMultiple(EggIncBoostTypeEnum boostType, List<EggIncArtifactInstance> artifacts, bool enlightenment) {
            var rate = 1.0;

            artifacts.ForEach(x => {
                if(x is null) {
                    return;
                }
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
            double rate = artifact.Value - 1;
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

        public static string GetFamilyShorthand(Family family) {
            return family.id switch {
                "puzzle-cube" => "cube",
                "lunar-totem" => "totem",
                "demeters-necklace" => "necklace",
                "vial-of-martian-dust" => "vial",
                "aurelian-brooch" => "brooch",
                "tungsten-ankh" => "ankh",
                "gusset" => "gusset",
                "neodymium-medallion" => "medallion",
                "mercurys-lens" => "lens",
                "beak-of-midas" => "beak",
                "carved-rainstick" => "rainstick",
                "interstellar-compass" => "compass",
                "the-chalice" => "chalice",
                "phoenix-feather" => "feather",
                "quantum-metronome" => "metronome",
                "dilithium-monocle" => "monocle",
                "titanium-actuator" => "actuator",
                "ship-in-a-bottle" => "ship",
                "tachyon-deflector" => "deflector",
                "book-of-basan" => "book",
                "light-of-eggendil" => "light",
                /*"lunar-stone" => "",
                "shell-stone" => "",
                "tachyon-stone" => "",
                "terra-stone" => "",
                "soul-stone" => "",
                "dilithium-stone" => "",
                "quantum-stone" => "",
                "life-stone" => "",
                "clarity-stone" => "",
                "prophecy-stone" => "",*/
                "gold-meteorite" => "meteorite",
                "tau-ceti-geode" => "geode",
                "solar-titanium" => "titanium",
                _ => family.name
            };
        }

        public static Tier GetTier(int afxId, int tierNumber) {
            var data = GetEiAfxData();
            // Should be do so, because stone fragment tiers have a different afx_id.
            var artifact = data.artifact_families.FirstOrDefault(x => x.tiers.Any(y => y.afx_id == afxId)) 
                ?? throw new Exception("Unable to locate artifact family with afx_id: " + afxId);

            var tier = artifact.tiers.FirstOrDefault(x => x.tier_number == tierNumber)
                ?? throw new Exception($"Unable to locate tier {tierNumber} for {artifact.name}");

            return tier;
        }

        public static int SlotCount(EggIncArtifactInstance instance) {
            var data = GetEiAfxData();
            var artifact = data.artifact_families.FirstOrDefault(x => x.name.Equals(instance.Artifact, StringComparison.OrdinalIgnoreCase)) 
                ?? throw new Exception("Unable to locate artifact family: " + instance.Artifact);

            var tier = artifact.tiers.FirstOrDefault(x => x.tier_number == instance.Tier) 
                ?? throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}");

            if(!tier.has_rarities) return 0;
            var rarity = tier.effects.FirstOrDefault(x => x.afx_rarity == instance.Rarity - 1);
            return rarity is null
                ? throw new Exception($"Unable to locate rarity {instance.Rarity} for {instance.Artifact} with tier {instance.Tier}")
                : rarity.slots ?? 0;
        }

        public static string GetProperNameFromJson(EggIncArtifactInstance instance) {
            var data = GetEiAfxData();
            var artifact = data.artifact_families.FirstOrDefault(x => x.name.Equals(instance.Artifact, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("Unable to locate artifact family: " + instance.Artifact);
            var tier = artifact.tiers.FirstOrDefault(x => x.tier_number == instance.Tier);
            return tier is null ? throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}") : tier.name;
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

            var tier = artifact.tiers.FirstOrDefault(x => x.tier_number == instance.Tier + (artifact.type.Equals("Stone", StringComparison.OrdinalIgnoreCase) && !isFragment ? 1 : 0));
            return tier is null ? throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}") : tier.name;
        }

        public static string GetNameFromJson(EggIncArtifactInstance instance) {
            var data = GetEiAfxData();
            var artifact = data.artifact_families.FirstOrDefault(x => x.name.Equals(instance.Artifact, StringComparison.OrdinalIgnoreCase));
            return artifact is null ? throw new Exception("Unable to locate artifact family: " + instance.Artifact) : artifact.id;
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
            
            var artifact = GetArtifactsDB.FirstOrDefault(x => (int)x.Name == (int)artifactSpec.Name);
            if(artifact == null)
                return null;
            var response = new EggIncArtifactInstance {
                Additive = artifact.Additive,
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
                Name = ArtifactNames.AurelianBrooch, Boost = EggIncBoostTypeEnum.DroneRewards, //done
                L0R0 = 1.1f,
                L1R0 = 1.25f,
                L2R0 = 1.5f, L2R1 = 1.6f, L2R2 = 1.7f,
                L3R0 = 2, L3R1 = 2.25f, L3R2 = 2.5f, L3R3 = 3
            },
            new EggIncArtifact {
                Name = ArtifactNames.BeakOfMidas, Boost = EggIncBoostTypeEnum.DroneRewards, //done
                L0R0 = 1.2f,
                L1R0 = 1.5f,
                L2R0 = 2, L2R1 = 3,
                L3R0 = 6, L3R1 = 11, L3R3 = 9999
            },
            new EggIncArtifact {
                Name = ArtifactNames.BookOfBasan, Boost = EggIncBoostTypeEnum.EggsOfProphecyEffect, //done
                L0R0 = 1.0025f,
                L1R0 = 1.005f,
                L2R0 = 1.0075f, L2R2 = 1.008f,
                L3R0 = 1.01f, L3R2 = 1.1f, L3R3 = 1.2f
            },
            new EggIncArtifact {
                Name = ArtifactNames.CarvedRainstick, Boost = EggIncBoostTypeEnum.CashGiftChance, //done
                L0R0 = 1.2f,
                L1R0 = 1.5f,
                L2R2 = 2,
                L3R0 = 5, L3R2 = 10, L3R3 = 9999
            },
            new EggIncArtifact {
                Name = ArtifactNames.ClarityStone, Boost = EggIncBoostTypeEnum.HostArtifactsOnElightenment, //done
                L0R0 = 0.25f,
                L1R0 = 0.50f,
                L2R0 = 1
            },
            new EggIncArtifact {
                Name = ArtifactNames.DemetersNecklace, Boost = EggIncBoostTypeEnum.EggValue, //done
                L0R0 = 1.1f,
                L1R0 = 1.25f, L1R1 = 1.35f,
                L2R0 = 1.5f, L2R1 = 1.6f, L2R2 = 1.75f,
                L3R0 = 2, L3R1 = 2.25f, L3R2 = 2.5f, L3R3 = 3
            },
            new EggIncArtifact {
                Name = ArtifactNames.DilithiumMonocle, Boost = EggIncBoostTypeEnum.BoostEffectiveness, //done
                L0R0 = 1.05f,
                L1R0 = 1.1f,
                L2R0 = 1.14f,
                L3R0 = 1.2f, L3R2 = 1.25f, L3R3 = 1.3f
            },
            new EggIncArtifact {
                Name = ArtifactNames.DilithiumStone, Boost = EggIncBoostTypeEnum.BoostDuration, //done
                L0R0 = 1.03f,
                L1R0 = 1.06f,
                L2R0 = 1.08f
            },
            new EggIncArtifact {
                Name = ArtifactNames.OrnateGusset, Boost = EggIncBoostTypeEnum.HabCapacity, //done
                L0R0 = 1.05f,
                L1R0 = 1.1f, L1R2 = 1.12f,
                L2R0 = 1.14f, L2R1 = 1.15f,
                L3R0 = 1.2f, L3R2 = 1.22f, L3R3 = 1.25f
            },
            new EggIncArtifact {
                Name = ArtifactNames.InterstellarCompass, Boost = EggIncBoostTypeEnum.EggShippingRate, //done
                L0R0 = 1.05f,
                L1R0 = 1.1f,
                L2R0 = 1.2f, L2R1 = 1.22f,
                L3R0 = 1.3f, L3R1 = 1.35f, L3R2 = 1.4f, L3R3 = 1.5f
            },
            new EggIncArtifact {
                Name = ArtifactNames.LifeStone, Boost = EggIncBoostTypeEnum.InternalHatchery, //done
                L0R0 = 1.02f,
                L1R0 = 1.03f,
                L2R0 = 1.04f
            },
            new EggIncArtifact {
                Name = ArtifactNames.LightOfEggendil, Boost = EggIncBoostTypeEnum.EnlightenmentEggValue, //done
                L0R0 = 1.5f,
                L1R0 = 2, L1R1 = 2.2f,
                L2R0 = 10, L2R1 = 15,
                L3R0 = 101, L3R2 = 151, L3R3 = 251
            },
            new EggIncArtifact {
                Name = ArtifactNames.LunarStone, Boost = EggIncBoostTypeEnum.AwayEarnings, //done
                L0R0 = 1.2f,
                L1R0 = 1.3f,
                L2R0 = 1.4f
            },
            new EggIncArtifact {
                Name = ArtifactNames.LunarTotem, Boost = EggIncBoostTypeEnum.AwayEarnings, //done
                L0R0 = 1.5f,
                L1R0 = 2, L1R1 = 2.5f,
                L2R0 = 4, L2R1 = 5,
                L3R0 = 6, L3R1 = 8, L3R2 = 10
            },
            new EggIncArtifact {
                Name = ArtifactNames.MercurysLens, Boost = EggIncBoostTypeEnum.FarmValue, //done
                L0R0 = 1.1f,
                L1R0 = 1.2f, L1R1 = 1.22f,
                L2R0 = 1.5f, L2R1 = 1.55f,
                L3R0 = 2, L3R1 = 2.25f, L3R2 = 2.5f, L3R3 = 3
            },
            new EggIncArtifact {
                Name = ArtifactNames.NeodymiumMedallion, Boost = EggIncBoostTypeEnum.DroneFrequency, //done
                L0R0 = 1.1f,
                L1R0 = 1.25f, L1R1 = 1.3f,
                L2R0 = 1.5f, L2R2 = 1.6f,
                L3R0 = 2, L3R1 = 2.1f, L3R2 = 2.2f, L3R3 = 2.29f
            },
            new EggIncArtifact {
                Name = ArtifactNames.PhoenixFeather, Boost = EggIncBoostTypeEnum.SoulEggCollectionRate, //done
                L0R0 = 1.25f,
                L1R0 = 2,
                L2R0 = 5, L2R1 = 6,
                L3R0 = 10,
                L3R1 = 12,
                L3R3 = 15
            },
            new EggIncArtifact {
                Name = ArtifactNames.ProphecyStone, Boost = EggIncBoostTypeEnum.EggsOfProphecyEffect, //done
                L0R0 = 1.0005f,
                L1R0 = 1.001f,
                L2R0 = 1.0015f
            },
            new EggIncArtifact {
                Name = ArtifactNames.PuzzleCube, Boost = EggIncBoostTypeEnum.ResearchCost, //done
                L0R0 = 1.05f,
                L1R0 = 1.1f, L1R2 = 1.15f,
                L2R0 = 1.2f, L2R1 = 1.22f,
                L3R0 = 1.5f, L3R1 = 1.53f, L3R2 = 1.55f, L3R3 = 1.6f
            },
            new EggIncArtifact {
                Name = ArtifactNames.QuantumMetronome, Boost = EggIncBoostTypeEnum.EggLayingRate, //done
                L0R0 = 1.05f,
                L1R0 = 1.1f, L1R1 = 1.12f,
                L2R0 = 1.14999f, L2R1 = 1.17f, L2R2 = 1.2f,
                L3R0 = 1.25f, L3R1 = 1.27f, L3R2 = 1.3f, L3R3 = 1.35f
            },
            new EggIncArtifact {
                Name = ArtifactNames.QuantumStone, Boost = EggIncBoostTypeEnum.EggShippingRate, //done
                L0R0 = 1.02f,
                L1R0 = 1.04f,
                L2R0 = 1.05f
            },
            new EggIncArtifact {
                Name = ArtifactNames.ShellStone, Boost = EggIncBoostTypeEnum.EggValue, //done
                L0R0 = 1.05f,
                L1R0 = 1.08f,
                L2R0 = 1.10f
            },
            new EggIncArtifact {
                Name = ArtifactNames.ShipInABottle, Boost = EggIncBoostTypeEnum.CoopMembersEarnings, //done
                L0R0 = 1.2f,
                L1R0 = 1.3f,
                L2R0 = 1.5f, L2R1 = 1.6f,
                L3R0 = 1.7f, L3R1 = 1.8f, L3R2 = 1.9f, L3R3 = 2
            },
            new EggIncArtifact {
                Name = ArtifactNames.SoulStone, Boost = EggIncBoostTypeEnum.SoulEggBonus, //done
                L0R0 = 1.05f,
                L1R0 = 1.1f,
                L2R0 = 1.25f
            },
            new EggIncArtifact {
                Name = ArtifactNames.TachyonDeflector, Boost = EggIncBoostTypeEnum.CoopMembersEggLayingRates, //done
                L0R0 = 1.05f,
                L1R0 = 1.08f,
                L2R0 = 1.12f, L2R1 = 1.12f,
                L3R0 = 1.14f, L3R1 = 1.17f, L3R2 = 1.19f, L3R3 = 1.2f
            },
            new EggIncArtifact {
                Name = ArtifactNames.TachyonStone, Boost = EggIncBoostTypeEnum.EggLayingRate, //done
                L0R0 = 1.02f,
                L1R0 = 1.04f,
                L2R0 = 1.05f
            },
            new EggIncArtifact {
                Name = ArtifactNames.TerraStone, Boost = EggIncBoostTypeEnum.RunningChickenBonus, Additive = true, //done
                L0R0 = 10,
                L1R0 = 50,
                L2R0 = 100
            },
            new EggIncArtifact {
                Name = ArtifactNames.TheChalice, Boost = EggIncBoostTypeEnum.InternalHatchery, //done
                L0R0 = 1.05f,
                L1R0 = 1.1f, L1R2 = 1.14f,
                L2R0 = 1.2f, L2R1 = 1.23f, L2R2 = 1.25f,
                L3R0 = 1.3f, L3R2 = 1.35f, L3R3 = 1.4f
            },
            new EggIncArtifact {
                Name = ArtifactNames.TitaniumActuator, Boost = EggIncBoostTypeEnum.HoldToHatch, Additive = true, //done
                L0R0 = 1,
                L1R0 = 4,
                L2R0 = 6, L2R1 = 7,
                L3R0 = 10, L3R2 = 12, L3R3 = 15
            },
            new EggIncArtifact {
                Name = ArtifactNames.TungstenAnkh, Boost = EggIncBoostTypeEnum.EggValue, //done
                L0R0 = 1.1f,
                L1R0 = 1.25f, L1R1 = 1.28f,
                L2R0 = 1.5f, L2R1 = 1.75f, L2R3 = 2,
                L3R0 = 2, L3R1 = 2.25f, L3R3 = 2.5f
            },
            new EggIncArtifact {
                Name = ArtifactNames.VialMartianDust, Boost = EggIncBoostTypeEnum.MaxRunningChickenBonus, Additive = true, //done
                L0R0 = 10,
                L1R0 = 50, L1R1 = 60,
                L2R0 = 100, L2R2 = 150,
                L3R0 = 200, L3R1 = 300, L3R3 = 500
            },
            new EggIncArtifact { Name = ArtifactNames.ExtraterrestrialAluminum },
            new EggIncArtifact { Name = ArtifactNames.AncientTungsten},
            new EggIncArtifact { Name = ArtifactNames.SpaceRocks},
            new EggIncArtifact { Name = ArtifactNames.AlienWood },
            new EggIncArtifact { Name = ArtifactNames.GoldMeteorite},
            new EggIncArtifact { Name = ArtifactNames.TauCetiGeode },
            new EggIncArtifact { Name = ArtifactNames.CentaurianSteel},
            new EggIncArtifact { Name = ArtifactNames.EridaniFeather },
            new EggIncArtifact { Name = ArtifactNames.DroneParts },
            new EggIncArtifact { Name = ArtifactNames.CelestialBronze},
            new EggIncArtifact { Name = ArtifactNames.LalandeHide},
            new EggIncArtifact { Name = ArtifactNames.SolarTitanium},
            new EggIncArtifact { Name = ArtifactNames.TachyonStoneFragment },
            new EggIncArtifact { Name = ArtifactNames.DilithiumStoneFragment },
            new EggIncArtifact { Name = ArtifactNames.ShellStoneFragment },
            new EggIncArtifact { Name = ArtifactNames.LunarStoneFragment },
            new EggIncArtifact { Name = ArtifactNames.SoulStoneFragment},
            new EggIncArtifact { Name = ArtifactNames.ProphecyStoneFragment},
            new EggIncArtifact { Name = ArtifactNames.QuantumStoneFragment},
            new EggIncArtifact { Name = ArtifactNames.TerraStoneFragment },
            new EggIncArtifact { Name = ArtifactNames.LifeStoneFragment },
            new EggIncArtifact { Name = ArtifactNames.ClarityStoneFragment },
        };
    }

    public enum ArtifactNames {
        [Description("Lunar Totem")]
        LunarTotem = 0,
        [Description("Tachyon Stone")]
        TachyonStone = 1,
        [Description("Tachyon Stone Fragment")]
        TachyonStoneFragment = 2,
        [Description("Neodymium Medallion")]
        NeodymiumMedallion = 3,
        [Description("Beak of Midas")]
        BeakOfMidas = 4,
        [Description("Light of Eggendil")]
        LightOfEggendil = 5,
        [Description("Demeters Necklace")]
        DemetersNecklace = 6,
        [Description("Vial of Martian Dust")]
        VialMartianDust = 7,
        [Description("Gusset")]
        OrnateGusset = 8,
        [Description("The Chalice")]
        TheChalice = 9,
        [Description("Book of Basan")]
        BookOfBasan = 10,
        [Description("Phoenix Feather")]
        PhoenixFeather = 11,
        [Description("Tungsten Ankh")]
        TungstenAnkh = 12,
        [Description("Extraterrestrial Aluminum")]
        ExtraterrestrialAluminum = 13,
        [Description("Ancient Tungsten")]
        AncientTungsten = 14,
        [Description("Space Rocks")]
        SpaceRocks = 15,
        [Description("Alien Wood")]
        AlienWood = 16,
        [Description("Gold Meteorite")]
        GoldMeteorite = 17,
        [Description("Tau Ceti Geode")]
        TauCetiGeode = 18,
        [Description("Centaurian Steel")]
        CentaurianSteel = 19,
        [Description("Eridant Feather")]
        EridaniFeather = 20,
        [Description("Aurelian Brooch")]
        AurelianBrooch = 21,
        [Description("Carved Rainstick")]
        CarvedRainstick = 22,
        [Description("Puzzle Cube")]
        PuzzleCube = 23,
        [Description("Quantum Metronome")]
        QuantumMetronome = 24,
        [Description("Ship in a Bottle")]
        ShipInABottle = 25,
        [Description("Tachyon Deflector")]
        TachyonDeflector = 26,
        [Description("Interstellar Compass")]
        InterstellarCompass = 27,
        [Description("Dilithium Monocle")]
        DilithiumMonocle = 28,
        [Description("Titanium Actuator")]
        TitaniumActuator = 29,
        [Description("Mercury's Lens")]
        MercurysLens = 30,
        [Description("Dilithium Stone")]
        DilithiumStone = 31,
        [Description("Shell Stone")]
        ShellStone = 32,
        [Description("Lunar Stone")]
        LunarStone = 33,
        [Description("Soul Stone")]
        SoulStone = 34,
        [Description("Drone Parts")]
        DroneParts = 35,
        [Description("Quantum Stone")]
        QuantumStone = 36,
        [Description("Terra Stone")]
        TerraStone = 37,
        [Description("Life Stone")]
        LifeStone = 38,
        [Description("Prophecy Stone")]
        ProphecyStone = 39,
        [Description("Clarity Stone")]
        ClarityStone = 40,
        [Description("Celestial Bronze")]
        CelestialBronze = 41,
        [Description("Lalande Hide")]
        LalandeHide = 42,
        [Description("Solar Titanium")]
        SolarTitanium = 43,
        [Description("Dilithium Stone Fragment")]
        DilithiumStoneFragment = 44,
        [Description("Shell Stone Fragment")]
        ShellStoneFragment = 45,
        [Description("Lunar Stone Fragment")]
        LunarStoneFragment = 46,
        [Description("Soul Stone Fragment")]
        SoulStoneFragment = 47,
        [Description("Prophecy Stone Fragment")]
        ProphecyStoneFragment = 48,
        [Description("Quantum Stone Fragment")]
        QuantumStoneFragment = 49,
        [Description("Terra Stone Fragment")]
        TerraStoneFragment = 50,
        [Description("Life Stone Fragment")]
        LifeStoneFragment = 51,
        [Description("Clarity Stone Fragment")]
        ClarityStoneFragment = 52,

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
        [Key(2)] public float Value { get; set; }

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
            if(stonesAreEqual && Stones?.Count > 0) {
                for(var i = 0; i < (Stones?.Count ?? 0); i++) {
                    if(!other.Stones[i].Equals(Stones[i])) {
                        stonesAreEqual = false;
                        break;
                    }
                }
            }

            var match = Artifact == other.Artifact && Rarity == other.Rarity && Tier == other.Tier && Boost == other.Boost && Value == other.Value && Additive == other.Additive && stonesAreEqual;
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
        //public string Artifact { get; set; }
        public ArtifactNames Name { get; set; }
        public EggIncBoostTypeEnum Boost { get; set; }
        public float L0R0 { get; set; }
        public float L1R0 { get; set; }
        public float L1R1 { get; set; }
        public float L1R2 { get; set; }
        public float L2R0 { get; set; }
        public float L2R1 { get; set; }
        public float L2R2 { get; set; }
        public float L2R3 { get; set; }
        public float L3R0 { get; set; }
        public float L3R1 { get; set; }
        public float L3R2 { get; set; }
        public float L3R3 { get; set; }
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