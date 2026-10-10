using System;
using System.Collections.Generic;

using EGG9000.Common.Database;

namespace EGG9000.Common.Helpers {
    public static class AccountFormulas {
        public static double MerValue(double soulEggs, double eggsOfProphecy) {
            if(soulEggs <= 0 || !double.IsFinite(soulEggs)) return 0.0;
            var seQ = soulEggs / 1e18;
            return (91 * Math.Log10(seQ) + 200 - eggsOfProphecy) / 10;
        }

        public static ulong TotalGeInPiggyBank(ulong piggyBank, ulong numPiggyBreaks) {
            try {
                return numPiggyBreaks switch {
                    < 2 => (ulong)(piggyBank * 1.02),
                    < 3 => (ulong)(piggyBank * 1.25),
                    _ => piggyBank + (piggyBank * (10 * (numPiggyBreaks + 1) + 10) / 100 + 1)
                };
            } catch(OverflowException) {
                return ulong.MaxValue;
            }
        }

        public static int PeFromTrophies(List<uint> eggMedalLevel) {
            if(eggMedalLevel is null)
                return -1;
            if(eggMedalLevel.Count != 19)
                throw new Exception($"Unexpected number of trophies, should be 19 but instead got {eggMedalLevel.Count}");
            var count = 0;

            if(eggMedalLevel[(int)Ei.Egg.Edible - 1] >= (uint)TrophyLevel.Diamond) count += 5;
            if(eggMedalLevel[(int)Ei.Egg.Superfood - 1] >= (uint)TrophyLevel.Diamond) count += 4;
            if(eggMedalLevel[(int)Ei.Egg.Medical - 1] >= (uint)TrophyLevel.Diamond) count += 3;
            if(eggMedalLevel[(int)Ei.Egg.RocketFuel - 1] >= (uint)TrophyLevel.Diamond) count += 2;

            if(eggMedalLevel[(int)Ei.Egg.SuperMaterial - 1] >= (uint)TrophyLevel.Diamond) count += 1;
            if(eggMedalLevel[(int)Ei.Egg.Fusion - 1] >= (uint)TrophyLevel.Diamond) count += 1;
            if(eggMedalLevel[(int)Ei.Egg.Quantum - 1] >= (uint)TrophyLevel.Diamond) count += 1;
            if(eggMedalLevel[(int)Ei.Egg.Immortality - 1] >= (uint)TrophyLevel.Diamond) count += 1;
            if(eggMedalLevel[(int)Ei.Egg.Tachyon - 1] >= (uint)TrophyLevel.Diamond) count += 1;

            if(eggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Diamond) count += 10;
            if(eggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Platinum) count += 5;
            if(eggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Gold) count += 3;
            if(eggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Silver) count += 2;
            if(eggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Bronze) count += 1;

            return count;
        }
    }
}
