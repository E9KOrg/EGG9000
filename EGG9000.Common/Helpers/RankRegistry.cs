using Discord;

using EGG9000.Common.JsonData;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EGG9000.Bot.Helpers {
    public sealed class FarmerRank {
        public int Oom { get; init; }
        public string DisplayName { get; init; }
        public string ColorHex { get; init; }
        public string RoleName { get; init; }

        public bool IsInfinifarmer => Oom == 51;
        public int GroupBase => IsInfinifarmer ? 51 : (Oom / 3) * 3;
        public int SubRank => IsInfinifarmer ? 1 : (Oom % 3) + 1;
        public bool IsGroupLead => Oom == GroupBase;

        public Color Color => new(uint.Parse(ColorHex.Replace("#", ""), NumberStyles.HexNumber));
    }

    public static class RankRegistry {
        private sealed class FarmerRankDto {
            public int oom { get; set; }
            public string name { get; set; }
            public string color { get; set; }
        }

        private static readonly EmbeddedResource<List<FarmerRankDto>> _dtos =
            EmbeddedResource.Json<List<FarmerRankDto>>("farmer-ranks.json");

        private static readonly Lazy<IReadOnlyList<FarmerRank>> _all = new(Build);
        public static IReadOnlyList<FarmerRank> All => _all.Value;

        private static readonly Lazy<IReadOnlyList<FarmerRank>> _leads =
            new(() => All.Where(r => r.IsGroupLead).ToList());
        public static IReadOnlyList<FarmerRank> GroupLeads => _leads.Value;

        public static FarmerRank ForOom(int oom) => All[Math.Clamp(oom, 0, 51)];

        public static FarmerRank ForEB(double eb) {
            var oom = eb <= 0 ? 0 : (int)Math.Floor(Math.Log10(eb / 100));
            return ForOom(oom);
        }

        public static FarmerRank ForName(string name) =>
            All.FirstOrDefault(r =>
                string.Equals(r.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.RoleName, name, StringComparison.OrdinalIgnoreCase));

        private static IReadOnlyList<FarmerRank> Build() {
            var dtos = _dtos.Value.OrderBy(d => d.oom).ToList();
            var leadNames = dtos.Where(d => d.oom == 51 || d.oom % 3 == 0)
                .ToDictionary(d => d.oom == 51 ? 51 : d.oom, d => d.name);

            return dtos.Select(d => {
                var groupBase = d.oom == 51 ? 51 : (d.oom / 3) * 3;
                var subRank = d.oom == 51 ? 1 : (d.oom % 3) + 1;
                var roleName = d.oom == 51
                    ? "Infinifarmer"
                    : $"{leadNames[groupBase]} {Roman(subRank)}";
                return new FarmerRank {
                    Oom = d.oom,
                    DisplayName = d.name,
                    ColorHex = d.color,
                    RoleName = roleName
                };
            }).ToList();
        }

        private static string Roman(int subRank) => subRank switch { 1 => "I", 2 => "II", _ => "III" };
    }
}
