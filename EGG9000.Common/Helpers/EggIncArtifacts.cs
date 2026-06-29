using EGG9000.Common.Database;
using EGG9000.Common.Extensions;
using EGG9000.Common.JsonData.EiAfxData;

using EGG9000.Common.JsonData.EiStatics;
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
        }

        public static double GetEggValueMutiple(CustomFarm farm) {
            return GetMultiple(EggIncBoostTypeEnum.EggValue, farm);
        }

        // Delegates to the shared JsonData loader so eiafx-data.json is parsed and cached once. Kept as a
        // helper because several callers reference it; new code can call EiAfxDataRoot.Get() directly.
        public static EiAfxDataRoot GetEiAfxData() => EiAfxDataRoot.Get();

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

        // The tier number we show to players: 1-4 for artifacts, 2-4 for stones (a stone's stored Tier is
        // zero-based and one behind its displayed tier), and 1 for fragments. Mirrors the tier offset
        // logic in GetTierName so the "(T#)" label matches the resolved tier name.
        public static int GetDisplayTier(EggIncArtifactInstance instance) {
            var isFragment = instance.Artifact.Contains("Fragment", StringComparison.OrdinalIgnoreCase);
            var family = ResolveFamily(instance, isFragment);
            var isStone = family is not null && family.type.Equals("Stone", StringComparison.OrdinalIgnoreCase);
            return instance.Tier + (isStone && !isFragment ? 1 : 0);
        }

        // The artifact's in-game effect for its exact tier and rarity, as the game presents it: a value
        // string ("+20%", "+1000%", "Guaranteed") and the thing it affects ("internal hatchery rate").
        // Both come straight from eiafx-data.json, so they read exactly like the in-game artifact card.
        // Returns null for fragments and anything without a matching effect row.
        public static (string Size, string Target)? GetEffectDisplay(EggIncArtifactInstance instance) {
            var effect = ResolveEffect(instance);
            if(effect is null || string.IsNullOrWhiteSpace(effect.effect_target)) return null;
            return (effect.effect_size, effect.effect_target);
        }

        // The raw bonus fraction the game applies for this instance's exact tier and rarity (e.g. 1.3 for a
        // +130% medallion, 500 for a +500 vial). Read straight from eiafx-data so artifact Values stay in
        // sync with the game instead of being hand-typed. Null when the instance can't be resolved.
        public static double? GetEffectDelta(EggIncArtifactInstance instance) {
            var effect = ResolveEffect(instance);
            return effect?.effect_delta;
        }

        // Resolves the single effect row (tier + rarity) for an instance, shared by the display and value
        // lookups so there is one tier/rarity resolution path. Stored rarity is 1-based (1 = Common); the
        // data file is 0-based. Falls back to the base (afx_rarity 0) row, then to whatever the tier offers.
        private static Effect ResolveEffect(EggIncArtifactInstance instance) {
            var isFragment = instance.Artifact.Contains("Fragment", StringComparison.OrdinalIgnoreCase);
            if(isFragment) return null;

            var family = ResolveFamily(instance, isFragment);
            if(family is null) return null;

            var tierNumber = GetDisplayTier(instance);
            var tier = family.tiers.FirstOrDefault(x => x.tier_number == tierNumber);
            if(tier?.effects is null || tier.effects.Count == 0) return null;

            return tier.effects.FirstOrDefault(e => e.afx_rarity == instance.Rarity - 1)
                ?? tier.effects.FirstOrDefault(e => e.afx_rarity == 0)
                ?? tier.effects[0];
        }

        // The value the game uses for a "Guaranteed" effect (e.g. T4L Beak/Rainstick). Stored as a large
        // multiplier so rate maths still work; ArtifactDisplay reads it back to show the "Guaranteed" label
        // rather than a literal "9999x".
        public const double GuaranteedSentinel = 9999;

        // Turns a JSON effect_delta into the multiplier we store on an instance: additive artifacts and the
        // clarity stone keep the raw delta, "Guaranteed" effects use the sentinel display relies on, and
        // everything else is a 1 + delta multiplier.
        private static float ValueFromDelta(EggIncArtifact meta, Effect effect) {
            if(string.Equals(effect.effect_size?.Trim(), "Guaranteed", StringComparison.OrdinalIgnoreCase)) {
                return (float)GuaranteedSentinel;
            }
            if(meta.Additive || meta.Boost == EggIncBoostTypeEnum.HostArtifactsOnElightenment) {
                return (float)effect.effect_delta;
            }
            return (float)(1 + effect.effect_delta);
        }

        private static ArtifactFamily ResolveFamily(EggIncArtifactInstance instance, bool isFragment) {
            var data = GetEiAfxData();
            var lookupName = isFragment
                ? instance.Artifact.Replace("Fragment", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd()
                : instance.Artifact;
            return data.artifact_families.FirstOrDefault(x => x.name.Equals(lookupName, StringComparison.OrdinalIgnoreCase));
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
                //Spec = artifactSpec
            };

            var effect = ResolveEffect(response);
            if(effect is not null) {
                response.Value = ValueFromDelta(artifact, effect);
            }

            if(response.Value == 0 && !response.Additive) {
                response.Value = 1;
            }

            return response;
        }

        // Boost type + additive flag per family. Per-tier/per-rarity values come from eiafx-data.json at
        // lookup time (GetArtifact). Ingredient families carry no boost.
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

    // Metadata bridge for an artifact family: the boost type and additive flag the JSON data file does not
    // carry. Per-tier/per-rarity values are sourced from eiafx-data.json at lookup time (see GetArtifact),
    // not stored here.
    public class EggIncArtifact {
        public ArtifactNames Name { get; set; }
        public EggIncBoostTypeEnum Boost { get; set; }
        public bool Additive { get; set; }
    }
}
