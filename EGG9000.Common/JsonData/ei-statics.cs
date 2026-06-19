using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace EGG9000.Common.JsonData.EiStatics {

    public class EggIncEgg {
        public int id { get; set; }
        public string name { get; set; }
        public string emoji { get; set; }
        public string imageUrlEnder { get; set; }
        public string image { 
            get {
                if(imageUrlEnder.StartsWith("http"))
                    return imageUrlEnder;
                return "https://vignette.wikia.nocookie.net/egg-inc/images/" + imageUrlEnder;
            }
        }
        public double value { get; set; }
        public double requiredFarmValue { get; set; }
    }

    public class  EggIncBoost {
        public string id { get; set; }
        public string name { get; set; }
        public string typeString { get; set; }
        public EggIncBoostTypeEnum type {
            get {
                if(Enum.TryParse<EggIncBoostTypeEnum>(typeString, ignoreCase: true, out var parsedEnum)) return parsedEnum;

                return EggIncBoostTypeEnum.UnlimitedHatchery;
            }
        }
        public string emoji { get; set; }
        public int timeMinutesInt {  get; set; }
        public TimeSpan timeMinutes {
            get {
                return TimeSpan.FromMinutes(timeMinutesInt);
            }
        }
        public int value { get; set; }
        public int? costGoldenEggs { get; set; }
        public int? costTokens {  get; set; }
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
        [Description("Co-op Earnings")]
        CoopMembersEarnings,
        [Description("Co-op Egg Rate")]
        CoopMembersEggLayingRates,
        SoulEggBonus,
        RunningChickenBonus,
        MaxRunningChickenBonus,
        HoldToHatch
    }

    public class Root {
        public List<EggIncBoost> eggIncBoosts { get; set; }
        public List<EggIncEgg> eggIncEggs { get; set; }
        private static readonly EmbeddedResource<Root> _res =
            EmbeddedResource.Json<Root>("ei-statics.json");
        public static Root Get() => _res.Value;
    }
}
