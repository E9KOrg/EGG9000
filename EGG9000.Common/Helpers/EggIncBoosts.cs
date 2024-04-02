using System;
using System.Collections.Generic;
using System.Linq;

using static EGG9000.Common.Helpers.EggIncBoostTypeEnum;

namespace EGG9000.Common.Helpers {
    public class EggIncBoosts {
        public static EggIncBoost FromId(string id) {
            return GetBoosts().FirstOrDefault(x => x.Id == id);
        }

        public static List<EggIncBoost> GetBoosts() {
            return [
                new () { Id = "quantum_bulb", Type = UnlimitedHatchery, Name = "Quantum Bulb", Emoji = "<:Quantum_bulb:724397149118267424>", Time = TimeSpan.FromMinutes(10), Value = 0, CostGoldenEggs = 10, CostTokens = 0 },
                new () { Id = "jimbos_blue", Type = Earnings, Name = "Jimbo's Excellent Bird Feed", Emoji = "<:Jimbos_10min_Bird:751626448644734986>", Time = TimeSpan.FromMinutes(60), Value = 2, CostGoldenEggs = 50, CostTokens = 1 },
                new () { Id = "jimbos_blue_v2", Type = Earnings, Name = "Jimbo's Excellent Bird Feed", Emoji = "<:Jimbos_10min_Bird:751626448644734986>", Time = TimeSpan.FromMinutes(60), Value = 2, CostGoldenEggs = 50, CostTokens = 1 },
                new () { Id = "jimbos_blue_big", Type = Earnings, Name = "Jimbo's Excellent Bird Feed", Emoji = "<:Jimbos_10min_Bird:751626448644734986>", Time = TimeSpan.FromMinutes(480), Value = 2, CostTokens = 1 },
                new () { Id = "jimbos_purple", Type = Earnings, Name = "Jimbo's Premium Bird Feed", Emoji = "<:Jimbos_10min_Bird:751626448644734986>", Time = TimeSpan.FromMinutes(30), Value = 10, CostGoldenEggs = 250, CostTokens = 2 },
                new () { Id = "jimbos_purple_v2", Type = Earnings, Name = "Jimbo's Premium Bird Feed", Emoji = "<:Jimbos_10min_Bird:751626448644734986>", Time = TimeSpan.FromMinutes(30), Value = 10, CostGoldenEggs = 250, CostTokens = 2 },
                new () { Id = "jimbos_purple_big", Type = Earnings, Name = "Jimbo's Premium Bird Feed", Emoji = "<:Jimbos_10min_Bird:751626448644734986>", Time = TimeSpan.FromMinutes(120), Value = 10, CostGoldenEggs = 750, CostTokens = 3 },
                new () { Id = "jimbos_orange", Type = Earnings, Name = "Jimbo's Best Bird Feed", Emoji = "<:Jimbos_10min_Bird:751626448644734986>", Time = TimeSpan.FromMinutes(10), Value = 50, CostGoldenEggs = 2500, CostTokens = 3 },
                new () { Id = "jimbos_orange_big", Type = Earnings, Name = "Jimbo's Best Bird Feed", Emoji = "<:Jimbos_60min_Bird:751625095230914662>", Time = TimeSpan.FromMinutes(120), Value = 50, CostGoldenEggs = 7500, CostTokens = 5 },
                new () { Id = "tachyon_prism_blue", Type = InternalHatchery, Name = "Tachyon Prism", Emoji = "<:Legendary_tachyon_prism:724391780598153236>", Time = TimeSpan.FromMinutes(30), Value = 10, CostGoldenEggs = 100, CostTokens = 1 },
                new () { Id = "tachyon_prism_blue_v2", Type = InternalHatchery, Name = "Tachyon Prism", Emoji = "<:Legendary_tachyon_prism:724391780598153236>", Time = TimeSpan.FromMinutes(30), Value = 10, CostGoldenEggs = 100, CostTokens = 1 },
                new () { Id = "tachyon_prism_blue_big", Type = InternalHatchery, Name = "Large Tachyon Prism", Emoji = "<:Legendary_tachyon_prism:724391780598153236>", Time = TimeSpan.FromMinutes(240), Value = 10, CostGoldenEggs = 600, CostTokens = 3 },
                new () { Id = "tachyon_prism_purple", Type = InternalHatchery, Name = "Powerful Tachyon Prism", Emoji = "<:Legendary_tachyon_prism:724391780598153236>", Time = TimeSpan.FromMinutes(20), Value = 100, CostGoldenEggs = 750, CostTokens = 3 },
                new () { Id = "tachyon_prism_purple_v2", Type = InternalHatchery, Name = "Powerful Tachyon Prism", Emoji = "<:Legendary_tachyon_prism:724391780598153236>", Time = TimeSpan.FromMinutes(20), Value = 100, CostGoldenEggs = 750, CostTokens = 3 },
                new () { Id = "tachyon_prism_purple_big", Type = InternalHatchery, Name = "Epic Tachyon Prism", Emoji = "<:Legendary_tachyon_prism:724391780598153236>", Time = TimeSpan.FromMinutes(120), Value = 100, CostGoldenEggs = 4000, CostTokens = 5 },
                new () { Id = "tachyon_prism_orange", Type = InternalHatchery, Name = "Legendary Tachyon Prism", Emoji = "<:Legendary_tachyon_prism:724391780598153236>", Time = TimeSpan.FromMinutes(10), Value = 1000, CostGoldenEggs = 15000, CostTokens = 8 },
                new () { Id = "tachyon_prism_orange_big", Type = InternalHatchery, Name = "Supreme Tachyon Prism", Emoji = "<:Legendary_tachyon_prism:724391780598153236>", Time = TimeSpan.FromMinutes(60), Value = 1000, CostGoldenEggs = 25000, CostTokens = 20 },
                new () { Id = "boost_beacon_blue", Type = BoostBeacon, Name = "Boost Beacon", Emoji = "<:Legendary_boost:748221091809591307>", Time = TimeSpan.FromMinutes(30), Value = 2, CostGoldenEggs = 1000, CostTokens = 5 },
                new () { Id = "boost_beacon_purple", Type = BoostBeacon, Name = "Epic Boost Beacon", Emoji = "<:Epic_Boost_Beacon:748220232971059291>", Time = TimeSpan.FromMinutes(10), Value = 10, CostGoldenEggs = 5000, CostTokens = 10 },
                new () { Id = "boost_beacon_blue_big", Type = BoostBeacon, Name = "Large Boost Beacon", Emoji = "<:Boost_Beacon5x:761737943152459778>", Time = TimeSpan.FromMinutes(60), Value = 5, CostGoldenEggs = 75000, CostTokens = 15 },
                new () { Id = "boost_beacon_orange", Type = BoostBeacon, Name = "Legendary Boost Beacon", Emoji = "<:Legendary_boost:748221091809591307>", Time = TimeSpan.FromMinutes(10), Value = 50, CostGoldenEggs = 30000, CostTokens = 50 },
                new () { Id = "soul_beacon_blue", Type = SoulEggs, Name = "Soul Beacon", Emoji = "<:Soul_beacon500x:724391496240988192>", Time = TimeSpan.FromMinutes(60), Value = 5, CostGoldenEggs = 250  },
                new () { Id = "soul_beacon_blue_v2", Type = SoulEggs, Name = "Soul Beacon", Emoji = "<:Soul_beacon500x:724391496240988192>", Time = TimeSpan.FromMinutes(60), Value = 5, CostGoldenEggs = 250  },
                new () { Id = "soul_beacon_purple", Type = SoulEggs, Name = "Epic Soul Beacon", Emoji = "<:Soul_beacon500x:724391496240988192>", Time = TimeSpan.FromMinutes(30), Value = 50, CostGoldenEggs = 2500 },
                new () { Id = "soul_beacon_purple_v2", Type = SoulEggs, Name = "Epic Soul Beacon", Emoji = "<:Soul_beacon500x:724391496240988192>", Time = TimeSpan.FromMinutes(30), Value = 50, CostGoldenEggs = 2500 },
                new () { Id = "soul_beacon_orange", Type = SoulEggs, Name = "Legendary Soul Beacon", Emoji = "<:Soul_beacon500x:724391496240988192>", Time = TimeSpan.FromMinutes(10), Value = 500, CostGoldenEggs = 15000  },
                new () { Id = "subsidy_application", Type = FarmValue, Name = "Subsidy Application", Emoji = "<:B_money_printer:730145142467723367>", Time = TimeSpan.FromMinutes(0), Value = 10, CostGoldenEggs = 150, CostTokens = 1 },
                new () { Id = "blank_check", Type = FarmValue, Name = "Blank Check", Emoji = "<:B_money_printer:730145142467723367>", Time = TimeSpan.FromMinutes(0), Value = 100, CostGoldenEggs = 500, CostTokens = 2 },
                new () { Id = "money_printer", Type = FarmValue, Name = "Money Printer", Emoji = "<:B_money_printer:730145142467723367>", Time = TimeSpan.FromMinutes(0), Value = 500, CostGoldenEggs = 2000, CostTokens = 3 },
                new () { Id = "soul_mirror_blue", Type = SoulMirror, Name = "Soul Mirror", Emoji = "<:Soul_Mirror:798884954670891059>", Time = TimeSpan.FromMinutes(10), Value = 0, CostGoldenEggs = 100, CostTokens = 5 },
                new () { Id = "soul_mirror_purple", Type = SoulMirror, Name = "Epic Soul Mirror", Emoji = "<:Soul_Mirror:798884954670891059>", Time = TimeSpan.FromMinutes(60), Value = 0, CostGoldenEggs = 500, CostTokens = 25 },
                new () { Id = "soul_mirror_orange", Type = SoulMirror, Name = "Legendary Soul Mirror", Emoji = "<:Soul_Mirror:798884954670891059>", Time = TimeSpan.FromMinutes(1440), Value = 0, CostGoldenEggs = 2000, CostTokens = 50 },
                new () { Id = "dilithium_bulb", Type = UnlimitedHatchery, Name = "Dilithium Warming Bulb", Emoji = "<:dili_bulb:1116601590003023882>", Time = TimeSpan.FromMinutes(10), Value = 0, CostGoldenEggs = 1000, CostTokens = 0 }   
            ];
        }

    }

    public class EggIncBoost {
        public string Id { get; set; }
        public EggIncBoostTypeEnum Type { get; set; }
        public string Name { get; set; }
        public string Emoji { get; set; }
        public TimeSpan Time { get; set; }
        public int Value { get; set; }
        public int? CostGoldenEggs { get; set; }
        public int? CostTokens { get; set; }
    }

    public enum EggIncBoostTypeEnum {
        UnlimitedHatchery,
        Earnings,
        InternalHatchery,
        BoostBeacon,
        SoulEggs,
        FarmValue,
        SoulMirror,


        DroneRewards,
        GoldRewardChance,
        EggsOfProphecyEffect,
        CashGiftChance,
        HostArtifactsOnElightenment,
        EggValue,
        BoostEffectiveness,
        BoostDuration,
        HabCapacity,
        EggShippingRate,
        EnlightenmentEggValue,
        AwayEarnings,
        DroneFrequency,
        SoulEggCollectionRate,
        ResearchCost,
        EggLayingRate,
        CoopMembersEarnings,
        CoopMembersEggLayingRates,
        SoulEggBonus,
        RunningChickenBonus,
        MaxRunningChickenBonus,
        HoldToHatch
    }
}
