using EggSharp.GameData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace EGG9000.Common.JsonData {

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

    public class EggIncBoost {
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
        public int timeMinutesInt { get; set; }
        public TimeSpan timeMinutes {
            get {
                return TimeSpan.FromMinutes(timeMinutesInt);
            }
        }
        public int value { get; set; }
        public int? costGoldenEggs { get; set; }
        public int? costTokens { get; set; }
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
            EmbeddedResource.Json<Root>("ei-statics.json", ApplyGameData);
        public static Root Get() => _res.Value;

        private static Root ApplyGameData(Root root) {
            var provider = EGG9000.Common.Helpers.GameDataSource.Provider;
            var eggValues = provider.EggCatalog.Eggs
                .Where(e => e.Name is not null)
                .ToDictionary(e => NormalizeEggName(e.Name), e => e.BaseValue);
            foreach(var egg in root.eggIncEggs) {
                if(eggValues.TryGetValue(NormalizeEggName(egg.name), out var value))
                    egg.value = value;
            }
            foreach(var boost in root.eggIncBoosts) {
                if(provider.Resolve(Families.Boost, boost.id) is { } effect) {
                    boost.timeMinutesInt = effect.MetaInt("durationSeconds") / 60;
                    var multiplier = effect.TryMeta("multiplier", out var m) ? Convert.ToDouble(m) : effect.Magnitude;
                    boost.value = (int)Math.Round(boost.type == EggIncBoostTypeEnum.FarmValue ? multiplier * 100 : multiplier);
                }
                if(provider.BoostCatalog.Find(boost.id) is { } entry) {
                    boost.costGoldenEggs = entry.Price ?? boost.costGoldenEggs;
                    boost.costTokens = entry.TokenPrice ?? boost.costTokens;
                }
            }
            return root;
        }

        private static string NormalizeEggName(string name) =>
            new([.. name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
    }
}
