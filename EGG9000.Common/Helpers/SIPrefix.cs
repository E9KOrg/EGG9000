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
                    if(Name == "infini") return "Infinifarmer";
                    return $"{Rank} {(SubRank == 1 ? "I" : SubRank == 2 ? "II" : "III")}";
                }
            }
            public string Rank {
                get {
                    return string.IsNullOrEmpty(Name) ? "Farmer" : Name.FirstCharToUpper() + "farmer";
                }
            }
        }

        public static List<PrefixDetails> Prefixes {
            get {
                return [.. RankRegistry.GroupLeads.Select(lead => new PrefixDetails {
                    Name = lead.DisplayName.Replace("farmer", "", StringComparison.OrdinalIgnoreCase).ToLower(),
                    Base = lead.Oom
                })];
            }
        }

        /// <summary>
        /// Returns the list of valid farmer role names (Farmer I through Infinifarmer), sourced from RankRegistry.
        /// </summary>
        public static List<string> GetAllFarmerRoles() {
            return [.. RankRegistry.All.Select(r => r.RoleName)];
        }
    }
}
