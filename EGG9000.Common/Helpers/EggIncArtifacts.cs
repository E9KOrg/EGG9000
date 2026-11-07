using EGG9000.Common.Database;
using EGG9000.Common.Extensions;
using EGG9000.Common.JsonData;
using MessagePack;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

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
                    x.Stones = [];
                double farmMultiple = (enlightenment && x.Boost != EggIncBoostTypeEnum.EnlightenmentEggValue) ? 0 : 1;
                farmMultiple += x.Stones.Where(s => s.Boost == EggIncBoostTypeEnum.HostArtifactsOnElightenment).Sum(s => s.Value);
                if(x.Boost == boostType) {
                    rate *= GetEnlightenmentRate(x, farmMultiple);
                }

                foreach(var stone in x.Stones.Where(x => x.Boost == boostType)) {
                    rate *= GetEnlightenmentRate(stone, farmMultiple);
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
        }

        public static double GetEggValueMutiple(CustomFarm farm) {
            return GetMultiple(EggIncBoostTypeEnum.EggValue, farm);
        }

        public static string GetFamilyShorthand(ArtifactFamilyEntry family) {
            return family.Id switch {
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
                "gold-meteorite" => "meteorite",
                "tau-ceti-geode" => "geode",
                "solar-titanium" => "titanium",
                _ => family.Name
            };
        }

        public static ArtifactTierEntry GetTier(int afxId, int tierNumber) {
            var artifact = ArtifactFamilies.Containing(afxId)
                ?? throw new Exception("Unable to locate artifact family with afx_id: " + afxId);

            return artifact.Tiers.FirstOrDefault(x => x.TierNumber == tierNumber)
                ?? throw new Exception($"Unable to locate tier {tierNumber} for {artifact.Name}");
        }

        public static int SlotCount(EggIncArtifactInstance instance) {
            var artifact = FamilyByName(instance.Artifact)
                ?? throw new Exception("Unable to locate artifact family: " + instance.Artifact);

            var tier = artifact.Tiers.FirstOrDefault(x => x.TierNumber == instance.Tier)
                ?? throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}");

            if(!artifact.HasRarities) return 0;
            var rarity = tier.Effects.FirstOrDefault(x => x.Rarity == instance.Rarity - 1)
                ?? throw new Exception($"Unable to locate rarity {instance.Rarity} for {instance.Artifact} with tier {instance.Tier}");
            return rarity.StoneCap;
        }

        public static string GetProperNameFromJson(EggIncArtifactInstance instance) {
            var artifact = FamilyByName(instance.Artifact) ?? throw new Exception("Unable to locate artifact family: " + instance.Artifact);
            var tier = artifact.Tiers.FirstOrDefault(x => x.TierNumber == instance.Tier);
            return tier is null ? throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}") : tier.Name;
        }

        public static string GetTierName(EggIncArtifactInstance instance) {
            var isFragment = IsFragment(instance);
            var artifact = ResolveFamily(instance, isFragment)
                ?? throw new Exception("Unable to locate artifact family: " + instance.Artifact);

            var tier = artifact.Tiers.FirstOrDefault(x => x.TierNumber == instance.Tier + (artifact.IsStone && !isFragment ? 1 : 0));
            return tier is null ? throw new Exception($"Unable to locate tier {instance.Tier} for {instance.Artifact}") : tier.Name;
        }

        public static string GetNameFromJson(EggIncArtifactInstance instance) {
            var artifact = FamilyByName(instance.Artifact);
            return artifact is null ? throw new Exception("Unable to locate artifact family: " + instance.Artifact) : artifact.Id;
        }

        public static int GetDisplayTier(EggIncArtifactInstance instance) {
            var isFragment = IsFragment(instance);
            var family = ResolveFamily(instance, isFragment);
            return instance.Tier + (family is not null && family.IsStone && !isFragment ? 1 : 0);
        }

        public static (string Size, string Target)? GetEffectDisplay(EggIncArtifactInstance instance) {
            var resolved = ResolveEffect(instance);
            if(resolved is null || string.IsNullOrWhiteSpace(resolved.Value.Family.EffectTarget)) return null;
            return (resolved.Value.Effect.Size, resolved.Value.Family.EffectTarget);
        }

        public const double GuaranteedSentinel = 9999;

        private static (ArtifactFamilyEntry Family, ArtifactEffectEntry Effect)? ResolveEffect(EggIncArtifactInstance instance) {
            if(IsFragment(instance)) return null;

            var family = ResolveFamily(instance, false);
            if(family is null) return null;

            var tierNumber = GetDisplayTier(instance);
            var tier = family.Tiers.FirstOrDefault(x => x.TierNumber == tierNumber);
            if(tier is null || tier.Effects.Count == 0) return null;

            var effect = tier.Effects.FirstOrDefault(e => e.Rarity == instance.Rarity - 1)
                ?? tier.Effects.FirstOrDefault(e => e.Rarity == 0)
                ?? tier.Effects[0];
            return (family, effect);
        }

        private static float StoredValue(ArtifactEffectEntry effect) => effect.Guaranteed ? (float)GuaranteedSentinel : (float)effect.Value;

        private static bool IsFragment(EggIncArtifactInstance instance) => instance.Artifact.Contains("Fragment", StringComparison.OrdinalIgnoreCase);

        private static ArtifactFamilyEntry FamilyByName(string name) =>
            ArtifactFamilies.All.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static ArtifactFamilyEntry ResolveFamily(EggIncArtifactInstance instance, bool isFragment) {
            var lookupName = isFragment
                ? instance.Artifact.Replace("Fragment", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd()
                : instance.Artifact;
            return FamilyByName(lookupName);
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
        }


        public static double GetHabSpaceMultiple(CustomFarm farm) {
            return GetMultiple(EggIncBoostTypeEnum.HabCapacity, farm);
        }

        public static EggIncArtifactInstance GetArtifact(Ei.ArtifactSpec artifactSpec) {
            if(artifactSpec == null) return null;

            var artifact = GetArtifactsDB.FirstOrDefault(x => (int)x.Name == (int)artifactSpec.Name);
            if(artifact == null) return null;

            var response = new EggIncArtifactInstance {
                Additive = artifact.Additive,
                Boost = artifact.Boost,
                Tier = (byte)(artifactSpec.Level + 1),
                Rarity = (byte)(artifactSpec.Rarity + 1),
                Id = (byte)artifact.Name
            };

            if(ResolveEffect(response) is { } resolved) {
                response.Value = StoredValue(resolved.Effect);
            }

            if(response.Value == 0 && !response.Additive) {
                response.Value = 1;
            }

            return response;
        }

        public static readonly List<EggIncArtifact> GetArtifactsDB = [
           new() { Name = ArtifactNames.AurelianBrooch, Boost = EggIncBoostTypeEnum.DroneRewards },
           new() { Name = ArtifactNames.BeakOfMidas, Boost = EggIncBoostTypeEnum.DroneRewards },
           new() { Name = ArtifactNames.BookOfBasan, Boost = EggIncBoostTypeEnum.EggsOfProphecyEffect },
           new() { Name = ArtifactNames.CarvedRainstick, Boost = EggIncBoostTypeEnum.CashGiftChance },
           new() { Name = ArtifactNames.ClarityStone, Boost = EggIncBoostTypeEnum.HostArtifactsOnElightenment },
           new() { Name = ArtifactNames.DemetersNecklace, Boost = EggIncBoostTypeEnum.EggValue },
           new() { Name = ArtifactNames.DilithiumMonocle, Boost = EggIncBoostTypeEnum.BoostEffectiveness },
           new() { Name = ArtifactNames.DilithiumStone, Boost = EggIncBoostTypeEnum.BoostDuration },
           new() { Name = ArtifactNames.OrnateGusset, Boost = EggIncBoostTypeEnum.HabCapacity },
           new() { Name = ArtifactNames.InterstellarCompass, Boost = EggIncBoostTypeEnum.EggShippingRate },
           new() { Name = ArtifactNames.LifeStone, Boost = EggIncBoostTypeEnum.InternalHatchery },
           new() { Name = ArtifactNames.LightOfEggendil, Boost = EggIncBoostTypeEnum.EnlightenmentEggValue },
           new() { Name = ArtifactNames.LunarStone, Boost = EggIncBoostTypeEnum.AwayEarnings },
           new() { Name = ArtifactNames.LunarTotem, Boost = EggIncBoostTypeEnum.AwayEarnings },
           new() { Name = ArtifactNames.MercurysLens, Boost = EggIncBoostTypeEnum.FarmValue },
           new() { Name = ArtifactNames.NeodymiumMedallion, Boost = EggIncBoostTypeEnum.DroneFrequency },
           new() { Name = ArtifactNames.PhoenixFeather, Boost = EggIncBoostTypeEnum.SoulEggCollectionRate },
           new() { Name = ArtifactNames.ProphecyStone, Boost = EggIncBoostTypeEnum.EggsOfProphecyEffect },
           new() { Name = ArtifactNames.PuzzleCube, Boost = EggIncBoostTypeEnum.ResearchCost },
           new() { Name = ArtifactNames.QuantumMetronome, Boost = EggIncBoostTypeEnum.EggLayingRate },
           new() { Name = ArtifactNames.QuantumStone, Boost = EggIncBoostTypeEnum.EggShippingRate },
           new() { Name = ArtifactNames.ShellStone, Boost = EggIncBoostTypeEnum.EggValue },
           new() { Name = ArtifactNames.ShipInABottle, Boost = EggIncBoostTypeEnum.CoopMembersEarnings },
           new() { Name = ArtifactNames.SoulStone, Boost = EggIncBoostTypeEnum.SoulEggBonus },
           new() { Name = ArtifactNames.TachyonDeflector, Boost = EggIncBoostTypeEnum.CoopMembersEggLayingRates },
           new() { Name = ArtifactNames.TachyonStone, Boost = EggIncBoostTypeEnum.EggLayingRate },
           new() { Name = ArtifactNames.TerraStone, Boost = EggIncBoostTypeEnum.RunningChickenBonus, Additive = true },
           new() { Name = ArtifactNames.TheChalice, Boost = EggIncBoostTypeEnum.InternalHatchery },
           new() { Name = ArtifactNames.TitaniumActuator, Boost = EggIncBoostTypeEnum.HoldToHatch, Additive = true },
           new() { Name = ArtifactNames.TungstenAnkh, Boost = EggIncBoostTypeEnum.EggValue },
           new() { Name = ArtifactNames.VialMartianDust, Boost = EggIncBoostTypeEnum.MaxRunningChickenBonus, Additive = true },
           new() { Name = ArtifactNames.ExtraterrestrialAluminum },
           new() { Name = ArtifactNames.AncientTungsten},
           new() { Name = ArtifactNames.SpaceRocks},
           new() { Name = ArtifactNames.AlienWood },
           new() { Name = ArtifactNames.GoldMeteorite},
           new() { Name = ArtifactNames.TauCetiGeode },
           new() { Name = ArtifactNames.CentaurianSteel},
           new() { Name = ArtifactNames.EridaniFeather },
           new() { Name = ArtifactNames.DroneParts },
           new() { Name = ArtifactNames.CelestialBronze},
           new() { Name = ArtifactNames.LalandeHide},
           new() { Name = ArtifactNames.SolarTitanium},
           new() { Name = ArtifactNames.TachyonStoneFragment },
           new() { Name = ArtifactNames.DilithiumStoneFragment },
           new() { Name = ArtifactNames.ShellStoneFragment },
           new() { Name = ArtifactNames.LunarStoneFragment },
           new() { Name = ArtifactNames.SoulStoneFragment},
           new() { Name = ArtifactNames.ProphecyStoneFragment},
           new() { Name = ArtifactNames.QuantumStoneFragment},
           new() { Name = ArtifactNames.TerraStoneFragment },
           new() { Name = ArtifactNames.LifeStoneFragment },
           new() { Name = ArtifactNames.ClarityStoneFragment },
        ];
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
        [IgnoreMember]
        public string Artifact { get { return Enums.GetAttribute<DescriptionAttribute>((ArtifactNames)Id).Description; } }
        [Key(1)] public EggIncBoostTypeEnum Boost { get; set; }
        [Key(2)] public float Value { get; set; }

        [Key(3)] public bool Additive { get; set; }

        [Key(4)] public List<EggIncArtifactInstance> Stones { get; set; }
        [Key(5)] public byte Tier { get; set; }
        [Key(6)] public byte Rarity { get; set; }
        [Key(7)] public byte Id { get; set; }

        public override bool Equals(object other) {
            if(other is EggIncArtifactInstance instance) return Equals(instance);
            else return false;
        }

        public bool Equals(EggIncArtifactInstance other) {
            if(other == null) return false;
            else if(ReferenceEquals(this, other)) return true;

            var stonesAreEqual = (Stones?.Count ?? 0) == (other.Stones?.Count ?? 0);
            if(stonesAreEqual && Stones?.Count > 0) {
                for(var i = 0; i < (Stones?.Count ?? 0); i++) {
                    if(!other.Stones[i].Equals(Stones[i])) {
                        stonesAreEqual = false;
                        break;
                    }
                }
            }

            var match = Rarity == other.Rarity && Tier == other.Tier && Boost == other.Boost && Value == other.Value && Additive == other.Additive && stonesAreEqual && Id == other.Id;
            return match;
        }

        public override int GetHashCode() {
            unchecked {
                var hash = 17;
                hash = hash * 23 + Artifact.GetHashCode();
                hash = hash * 23 + Boost.GetHashCode();
                hash = hash * 23 + Value.GetHashCode();
                hash = hash * 23 + Additive.GetHashCode();
                hash = hash * 23 + Id.GetHashCode();
                foreach(var stone in Stones ?? []) {
                    hash = hash * 23 + stone.GetHashCode();
                }

                return hash;
            }
        }
    }

    public class EggIncArtifact {
        public ArtifactNames Name { get; set; }
        public EggIncBoostTypeEnum Boost { get; set; }
        public bool Additive { get; set; }
    }
}
