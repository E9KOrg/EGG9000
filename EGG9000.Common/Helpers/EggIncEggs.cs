using DiscordCoopCodes;
using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EGG9000.Common.Helpers {
    public class EggIncEggs {
        public static EggIncEgg GetEggById(Ei.Egg egg) {
            return GetEggById((int)egg);
        }
        public static EggIncEgg GetEggById(int id) {
            return GetEggs().FirstOrDefault(x => x.Id == id);
        }
        public static List<EggIncEgg> GetEggs() {
            return new List<EggIncEgg> {
                new EggIncEgg { Id = 1, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/e/e6/Egg_1.png", Emoji = "<:Egg_1:712424206276755516>", Name = "Edible", Value = 0.25, RequiredFarmValue = 0 },
new EggIncEgg { Id = 2, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/9/9f/Egg_2.png", Emoji = "<:Egg_12:712424230502793296>", Name = "Superfood", Value = 1.25, RequiredFarmValue = 67000000 },
new EggIncEgg { Id = 3, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/5/54/Egg_3.png", Emoji = "<:Egg_13:712424393908813924>", Name = "Medical", Value = 6.25, RequiredFarmValue = 5100000000 },
new EggIncEgg { Id = 4, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/e/ea/Egg_4.png", Emoji = "<:Egg_14:724340749289652317>", Name = "Rocket Fuel", Value = 30, RequiredFarmValue = 230000000000 },
new EggIncEgg { Id = 5, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/a/a8/Egg_5.png", Emoji = "<:Egg_15:712424174496514069>", Name = "Super Material", Value = 150, RequiredFarmValue = 150000000000000 },
new EggIncEgg { Id = 6, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/7/7f/Egg_6.png", Emoji = "<:Egg_16:711948447254577192>", Name = "Fusion", Value = 700, RequiredFarmValue = 4.5E+16 },
new EggIncEgg { Id = 7, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/2/29/Egg_7.png", Emoji = "<:Egg_17:712423375552643183>", Name = "Quantum", Value = 3000, RequiredFarmValue = 2E+19 },
new EggIncEgg { Id = 8, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/0/0f/Egg_8.png", Emoji = "<:Egg_18:720623152023076955>", Name = "Immortality", Value = 12500, RequiredFarmValue = 9.4E+22 },
new EggIncEgg { Id = 9, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/a/ac/Egg_9.png", Emoji = "<:Egg_19:712423407979069531>", Name = "Tachyon", Value = 50000, RequiredFarmValue = 1.7E+26 },
new EggIncEgg { Id = 10, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/a/a4/Egg_10.png", Emoji = "<:Egg_20:712423896481005569>", Name = "Graviton", Value = 175000, RequiredFarmValue = 9.8E+29 },
new EggIncEgg { Id = 11, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/9/9e/Egg_11.png", Emoji = "<:Egg_21:724337981791666247>", Name = "Dilithium", Value = 525000, RequiredFarmValue = 4.8E+33 },
new EggIncEgg { Id = 12, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/e/ee/Egg_12.png", Emoji = "<:Egg_22:720623498506272830>", Name = "Prodigy", Value = 1500000, RequiredFarmValue = 1.6E+37 },
new EggIncEgg { Id = 13, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/a/af/Egg_13.png", Emoji = "<:Egg_23:712422694368575498>", Name = "Terraform", Value = 10000000, RequiredFarmValue = 2E+40 },
new EggIncEgg { Id = 14, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/e/eb/Egg_14.png", Emoji = "<:Egg_24:724340137076588625>", Name = "Antimatter", Value = 1.00E+09, RequiredFarmValue = 1.3E+44 },
new EggIncEgg { Id = 15, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/a/a2/Egg_15.png", Emoji = "<:Egg_25:694347461581602827>", Name = "Dark Matter", Value = 1.00E+11, RequiredFarmValue = 1.2E+48 },
new EggIncEgg { Id = 16, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/a/ae/Egg_16.png", Emoji = "<:Egg_26:720623932427862087>", Name = "AI", Value = 1.00E+12, RequiredFarmValue = 3.2E+52 },
new EggIncEgg { Id = 17, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/2/21/Egg_17.png", Emoji = "<:Egg_27:724339043772661791>", Name = "Nebula", Value = 1.50E+13, RequiredFarmValue = 2E+56 },
new EggIncEgg { Id = 18, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/c/cd/Egg_18.png", Emoji = "<:Egg_28:724341409288814654>", Name = "Universe", Value = 1.00E+14, RequiredFarmValue = 4E+60 },
new EggIncEgg { Id = 19, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/a/ab/Egg_19.png", Emoji = "<:Egg_29:694345628486074409>", Name = "Enlightenment", Value = 0.0000001, RequiredFarmValue = 3.6E+65 },
new EggIncEgg { Id = 100, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/a/ac/Egg_chocolate.png", Emoji = "<:Egg_chocolate:724341622929883217> ", Name = "Chocolate", Value = 5},
new EggIncEgg { Id = 101, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/2/28/Egg_easter.png", Emoji = "<:Egg_easter:712423919436562494> ", Name = "Easter", Value = 0.05 },
new EggIncEgg { Id = 103, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/5/5e/Egg_firework.png", Emoji = "<:Egg_firework:724667535588327526> ", Name = "Firework", Value = 4.99},
new EggIncEgg { Id = 102, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/9/99/Egg_waterballoon.png", Emoji = "<:Egg_waterballoon:727297253685067816> ", Name = "Waterballoon", Value = 0.1},
new EggIncEgg { Id = 104, Image = "https://vignette.wikia.nocookie.net/egg-inc/images/1/11/Egg_pumpkin.png", Emoji = "<:Egg_pumpkin:730169310646894752> ", Name = "Pumpkin", Value = 0.99},
            };
        }

        private static string ToStartCase(string input) {
            var reg1 = new Regex(@"_");
            var reg2 = new Regex(@"(?: |\b)(\w)");
            input = reg1.Replace(input, " ");
            input = reg2.Replace(input, match => match.Value.ToUpper());
            return input;
        }

        public static string GetReward(Ei.Contract.Types.Goal goal) {
            switch (goal.RewardType) {
                case Ei.RewardType.EggsOfProphecy:
                    return $"{goal.RewardAmount} - <:Egg_of_Prophecy:669981330477547580> ";
                case Ei.RewardType.PiggyFill:
                    return $"<:Piggy_bank:724396277676113955> + {goal.RewardAmount.ToEggString()}";
                case Ei.RewardType.Gold:
                    return $"<:Egg_Golden:692439755798872075>  {goal.RewardAmount.ToEggString()}";
                case Ei.RewardType.EpicResearchItem:
                    return $"{ToStartCase(goal.RewardSubType)} +{goal.RewardAmount}";
                case Ei.RewardType.Boost:
                    var boost = EggIncBoosts.FromId(goal.RewardSubType);

                    var type = "";

                    switch (boost.Type) {
                        case EggIncBoostTypeEnum.Earnings:
                            type = $"{boost.Emoji} {boost.Value}x for {boost.Time.Humanize().ShortenTime()}";
                            break;
                        case EggIncBoostTypeEnum.BoostBeacon:
                            type = $"{boost.Emoji} {boost.Value}x for {boost.Time.Humanize().ShortenTime()}";
                            break;
                        case EggIncBoostTypeEnum.InternalHatchery:
                            type = $"{boost.Emoji} {boost.Value}x for {boost.Time.Humanize().ShortenTime()}";
                            break;
                        case EggIncBoostTypeEnum.FarmValue:
                            type = $"{boost.Emoji} +{boost.Value}% of Farm Value";
                            break;
                        case EggIncBoostTypeEnum.SoulEggs:
                            type = $"{boost.Emoji} {boost.Value}x for {boost.Time.Humanize().ShortenTime()}";
                            break;
                        case EggIncBoostTypeEnum.SoulMirror:
                            type = $"{boost.Emoji} for {boost.Time.Humanize().ShortenTime()}";
                            break;
                        case EggIncBoostTypeEnum.UnlimitedHatchery:
                            type = $"{boost.Emoji} for {boost.Time.Humanize().ShortenTime()}";
                            break;
                    }
                    return (goal.RewardAmount > 1 ? goal.RewardAmount.ToString() + " - " : "") + type;
                case Ei.RewardType.SoulEggs:
                    return $"{goal.RewardAmount.ToEggString()} <:Egg_soul:724341890794913964>";
                case Ei.RewardType.PiggyMultiplier:
                    return $"{goal.RewardAmount}x <:Piggy_bank:724396277676113955>";
                case Ei.RewardType.PiggyLevelBump:
                    return $"<:Piggy_up:812846540426838016> Level + {goal.RewardAmount}";
                case Ei.RewardType.ArtifactCase:
                    return $"{goal.RewardAmount} artifacts";
                default:
                    return goal.RewardType.ToString();
            }

        }
    }

    public class EggIncEgg {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Emoji { get; set; }
        public string Image { get; set; }
        public double Value { get; set; }
        public double RequiredFarmValue { get; set; }
    }
}
