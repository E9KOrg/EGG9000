using System;
using System.Linq;

using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiAfxData;
using EGG9000.Common.JsonData.EiStatics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    // Guards that artifact Values come from eiafx-data.json (effect_delta) rather than hand-typed numbers.
    // The regression case is the Neodymium Medallion T4 Legendary, which used to read 2.29x instead of the
    // correct 2.3x (effect_delta 1.3 -> 1 + 1.3).
    [TestClass]
    [TestCategory("Unit")]
    public class ArtifactValueTests {
        private static Ei.ArtifactSpec Spec(Ei.ArtifactSpec.Types.Name name, int level, int rarity) =>
            new Ei.ArtifactSpec {
                Name = name,
                Level = (Ei.ArtifactSpec.Types.Level)level,
                Rarity = (Ei.ArtifactSpec.Types.Rarity)rarity
            };

        [TestMethod]
        public void Medallion_T4Legendary_is_2point3_not_2point29() {
            var value = EggIncArtifacts.GetArtifact(Spec(Ei.ArtifactSpec.Types.Name.NeodymiumMedallion, 3, 3)).Value;
            Assert.AreEqual(2.3, value, 1e-4, "T4L medallion is +130% -> 2.3x");
        }

        [TestMethod]
        public void SpotChecks_matchInGameValues() {
            // Multiplier: necklace T4L +200% -> 3x.
            Assert.AreEqual(3.0, EggIncArtifacts.GetArtifact(Spec(Ei.ArtifactSpec.Types.Name.DemetersNecklace, 3, 3)).Value, 1e-4);
            // Additive: vial T4L +500 -> 500.
            Assert.AreEqual(500.0, EggIncArtifacts.GetArtifact(Spec(Ei.ArtifactSpec.Types.Name.VialMartianDust, 3, 3)).Value, 1e-4);
            // Guaranteed sentinel: beak T4L.
            Assert.AreEqual(EggIncArtifacts.GuaranteedSentinel, EggIncArtifacts.GetArtifact(Spec(Ei.ArtifactSpec.Types.Name.BeakOfMidas, 3, 3)).Value, 1e-4);
            // Clarity stone T1 stores raw delta 0.25.
            Assert.AreEqual(0.25, EggIncArtifacts.GetArtifact(Spec(Ei.ArtifactSpec.Types.Name.ClarityStone, 0, 0)).Value, 1e-4);
        }

        // Sweep every boost-bearing family x tier x rarity present in eiafx-data and assert GetArtifact
        // reproduces the JSON effect_delta via the documented formula. Makes the mapping self-checking
        // against future data updates.
        [TestMethod]
        public void EveryArtifactValue_derivesFromJsonDelta() {
            var data = EiAfxDataRoot.Get();

            foreach(var meta in EggIncArtifacts.GetArtifactsDB) {
                // Ingredients and anything not modelled by ArtifactSpec.Name are skipped.
                if(!Enum.TryParse<Ei.ArtifactSpec.Types.Name>(meta.Name.ToString(), out var specName)) continue;
                // Fragments share an afx_id with their stone but have no GetArtifact values; skip them.
                if(meta.Name.ToString().Contains("Fragment", StringComparison.OrdinalIgnoreCase)) continue;

                // Match the family by its own afx_id (the family header), not a child tier's afx_id, so a
                // fragment family with the same tier afx_id can't shadow the real artifact.
                var family = data.artifact_families.FirstOrDefault(f => f.afx_id == (int)specName);
                if(family is null) continue;

                foreach(var tier in family.tiers) {
                    if(tier.effects is null) continue;
                    var isStone = family.type.Equals("Stone", StringComparison.OrdinalIgnoreCase);
                    // Stone tier_number is one ahead of the stored 0-based level; artifacts line up directly.
                    var level = tier.tier_number - 1 - (isStone ? 1 : 0);
                    if(level < 0 || level > 3) continue;

                    foreach(var effect in tier.effects) {
                        var rarity = effect.afx_rarity;
                        if(rarity < 0 || rarity > 3) continue;

                        var expected = ExpectedValue(meta, effect);
                        var actual = EggIncArtifacts.GetArtifact(Spec(specName, level, rarity)).Value;
                        Assert.AreEqual(expected, actual, 1e-4,
                            $"{meta.Name} L{level}R{rarity}: delta {effect.effect_delta}, size '{effect.effect_size}'");
                    }
                }
            }
        }

        private static double ExpectedValue(EggIncArtifact meta, Effect effect) {
            if(string.Equals(effect.effect_size?.Trim(), "Guaranteed", StringComparison.OrdinalIgnoreCase)) return EggIncArtifacts.GuaranteedSentinel;
            var raw = meta.Additive
                || meta.Boost == EggIncBoostTypeEnum.HostArtifactsOnElightenment;
            var value = raw ? effect.effect_delta : 1 + effect.effect_delta;
            // GetArtifact coerces an unresolved 0 multiplier up to 1 for non-additive artifacts.
            if(value == 0 && !meta.Additive) value = 1;
            return value;
        }
    }
}
