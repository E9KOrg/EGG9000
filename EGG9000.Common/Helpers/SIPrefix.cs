using EGG9000.Common.Database;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Bot.Helpers {
    public class SIPrefix {

        public static IList<RankInfo> GetNextRankInfo(CustomBackup backup, bool withSubRank) {
            var ranks = new List<RankInfo>();

            var nextRank = GetPrefixFromEB((withSubRank ? 10 : 1000) * backup.EarningsBonus);
            var nextRankEB = Math.Pow(10, nextRank.Base + (withSubRank ? nextRank.SubRank - 1 : 0)) * 100;

            for(int i = 0; i <= 10; i++) {
                var totalSEForNextRank = nextRankEB
                    / (backup.SoulEggBonus * Math.Pow(backup.ProphecyEggBonus, backup.EggsOfProphecy + i));
                ranks.Add(new RankInfo {
                    Rank = withSubRank ? nextRank.RankWithSubRank : nextRank.Rank,
                    EggsOfProphecy = (ushort)i,
                    SoulsEggs = totalSEForNextRank - backup.SoulEggs,
                    EarningsBonus = nextRankEB
                });
            }

            return ranks;
        }


        public class RankInfo {
            public string Rank { get; set; }
            public double SoulsEggs { get; set; }
            public ushort EggsOfProphecy { get; set; }
            public double EarningsBonus { get; set; }

        }
        public static PrefixDetails GetPrefixFromEB(double number) {
            return GetPrefix(number / 100);
        }
        public static PrefixDetails GetPrefix(double number) {
            var exponent = number == 0 ? 0 : (int)Math.Floor(Math.Log10(Math.Abs(number)));
            var prefix = Prefixes.FirstOrDefault(x => exponent < x.Base + 3);
            prefix ??= Prefixes.Last();
            prefix.SubRank = exponent - prefix.Base + 1;
            return prefix;
        }

        public class PrefixDetails {
            public string Name { get; set; }
            public int Base { get; set; }
            public int SubRank { get; set; }
            public string RankWithSubRank {
                get {
                    return Name.FirstCharToUpper() + "farmer " + (SubRank == 1 ? "I" : SubRank == 2 ? "II" : "III");
                }
            }
            public string Rank {
                get {
                    return Name.FirstCharToUpper() + "farmer";
                }
            }
        }

        public static List<PrefixDetails> Prefixes {
            get {
                return [
                    new() { Name = "", Base = 0},
                    new() { Name="kilo", Base = 3},
                    new() { Name="mega", Base = 6},
                    new() { Name="giga", Base = 9},
                    new() { Name="tera", Base = 12},
                    new() { Name="peta", Base = 15},
                    new() { Name="exa", Base = 18},
                    new() { Name="zetta", Base = 21},
                    new() { Name="yotta", Base = 24},
                    new() { Name="xenna", Base = 27},
                    new() { Name="wecca", Base = 30},
                    new() { Name="venda", Base = 33},
                ];
            }
        }

        /// <summary>
        /// Method for returning list of valid farmer roles from Farmer to Vendafarmer III.
        /// </summary>
        /// <returns></returns>
        public static List<string> GetAllFarmerRoles() {
            return new List<string> {
                    "Farmer I",
                    "Farmer II",
                    "Farmer III",
                    "Kilofarmer I",
                    "Kilofarmer II",
                    "Kilofarmer III",
                    "Megafarmer I",
                    "Megafarmer II",
                    "Megafarmer III",
                    "Gigafarmer I",
                    "Gigafarmer II",
                    "Gigafarmer III",
                    "Terafarmer I",
                    "Terafarmer II",
                    "Terafarmer III",
                    "Petafarmer I",
                    "Petafarmer II",
                    "Petafarmer III",
                    "Exafarmer I",
                    "Exafarmer II",
                    "Exafarmer III",
                    "Zettafarmer I",
                    "Zettafarmer II",
                    "Zettafarmer III",
                    "Yottafarmer I",
                    "Yottafarmer II",
                    "Yottafarmer III",
                    "Xennafarmer I",
                    "Xennafarmer II",
                    "Xennafarmer III",
                    "Weccafarmer I",
                    "Weccafarmer II",
                    "Weccafarmer III",
                    "Vendafarmer I",
                    "Vendafarmer II",
                    "Vendafarmer III"
                };
        }
    }
}
