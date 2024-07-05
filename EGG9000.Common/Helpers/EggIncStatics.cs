using EGG9000.Bot;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.JsonData.EiStatics;
using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EGG9000.Common.Helpers {
    public class EggIncStatics {
        public static EggIncEgg GetEggByContract(Contract contract, List<DBCustomEgg> customEggs) {
            return GetEggById(contract.Details.Egg, contract, customEggs);
        }

        public static EggIncEgg GetEggById(Ei.Egg egg, Contract contract, List<DBCustomEgg> customEggs) {
            return GetEggById((int)egg, contract, customEggs);
        }

        public static EggIncEgg GetEggById(int id, Contract contract, List<DBCustomEgg> customEggs) {
           try {
                if(id == 200) {
                    var customEgg = customEggs.FirstOrDefault(ce => ce.Identifier == (contract.Details?.CustomEggId ?? "INVALID"));
                    var failBackEgg = contract.CustomEggs.First();
                    return new EggIncEgg {
                        value = customEgg?.Value ?? failBackEgg?.Value ?? 0,
                        imageUrlEnder = customEgg?.Icon.URL ?? failBackEgg?.Icon.Url ?? "",
                        emoji = customEgg is not null ? $"<{customEgg.EmojiName}:{customEgg.EmojiId}>" : "<:Edible_Egg:712424206276755516>"
                    };
                } else {
                    return Root.Get().eggIncEggs.FirstOrDefault(x => x.id == id);
                }
            } catch(Exception) {
                return null;
            }  
        }

        private static string ToStartCase(string input) {
            var reg1 = new Regex(@"_");
            var reg2 = new Regex(@"(?: |\b)(\w)");
            input = reg1.Replace(input, " ");
            input = reg2.Replace(input, match => match.Value.ToUpper());
            return input;
        }

        public static EggIncBoost FromId(string id) {
            try {
                return Root.Get().eggIncBoosts.FirstOrDefault(x => x.id == id);
            } catch(Exception) {
                return null;
            }
            
        }

        public static string GetReward(Ei.Contract.Types.Goal goal) {
            switch (goal.RewardType) {
                case Ei.RewardType.EggsOfProphecy:
                    return $"<:Egg_of_Prophecy:669981330477547580> {goal.RewardAmount}";
                case Ei.RewardType.PiggyFill:
                    return $"<:Piggy_bank:724396277676113955> + {goal.RewardAmount.ToEggString()}";
                case Ei.RewardType.Gold:
                    return $"<:Egg_Golden:692439755798872075>  {goal.RewardAmount.ToEggString()}";
                case Ei.RewardType.EpicResearchItem:
                    var researchItem = (JsonData.EIEpicResearch.Root.Get().epicResearchItems.AsQueryable().FirstOrDefault(x => x.id == goal.RewardSubType.ToLower()));
                    var goldenEggRefund = (int)(researchItem?.Costs?.Last() * goal?.RewardAmount);
                    var goldenEggRefundString = $" (<:Golden_Egg_GE:692439755798872075> {(goldenEggRefund >= 1000 ? (goldenEggRefund / 1000).ToString() + 'K' : goldenEggRefund)})";
                    return $"{researchItem?.title ?? ToStartCase(goal.RewardSubType)} +{goal.RewardAmount}{(goldenEggRefund == default ? "" : goldenEggRefundString)}";
                case Ei.RewardType.Boost:
                    var boost = FromId(goal.RewardSubType);

                    var type = (object)boost.type switch {
                        EggIncBoostTypeEnum.Earnings => $"{boost.emoji} {boost.value}x for {boost.timeMinutes.Humanize().ShortenTime()}",
                        EggIncBoostTypeEnum.BoostBeacon => $"{boost.emoji} {boost.value}x for {boost.timeMinutes.Humanize().ShortenTime()}",
                        EggIncBoostTypeEnum.InternalHatchery => $"{boost.emoji} {boost.value}x for {boost.timeMinutes.Humanize().ShortenTime()}",
                        EggIncBoostTypeEnum.FarmValue => $"{boost.emoji} {boost.value / 100}x of Farm Value",
                        EggIncBoostTypeEnum.SoulEggs => $"{boost.emoji} {boost.value}x for {boost.timeMinutes.Humanize().ShortenTime()}",
                        EggIncBoostTypeEnum.SoulMirror => $"{boost.emoji} for {boost.timeMinutes.Humanize().ShortenTime()}",
                        EggIncBoostTypeEnum.UnlimitedHatchery => $"{boost.emoji} for {boost.timeMinutes.Humanize().ShortenTime()}",
                        _ => $"Unknown Boost Type: {goal.RewardSubType}",
                    };
                    return (goal.RewardAmount > 1 ? goal.RewardAmount.ToString() : "") + type;
                case Ei.RewardType.SoulEggs:
                    return $"<:Egg_soul:724341890794913964> {goal.RewardAmount.ToEggString()}";
                case Ei.RewardType.PiggyMultiplier:
                    return $"<:Piggy_bank:724396277676113955> {goal.RewardAmount}x";
                case Ei.RewardType.PiggyLevelBump:
                    return $"<:Piggy_up:812846540426838016> Level + {goal.RewardAmount}";
                case Ei.RewardType.ArtifactCase:
                    return $"<:Afx_reward:877681508607987772> {goal.RewardAmount}";
                case Ei.RewardType.ShellScript:
                    return $"<:tickets:998630687831769189> {goal.RewardAmount}";
                default:
                    return goal.RewardType.ToString();
            }

        }
    }
}
