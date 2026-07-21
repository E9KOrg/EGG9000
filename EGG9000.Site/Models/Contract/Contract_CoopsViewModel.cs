using System.Collections.Generic;
using EGG9000.Common.Database.Entities;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Models.Contract {
    public class Contract_CoopsViewModel {
        public List<Coop> Coops { get; set; }
        public GuildContract GuildContract { get; set; }
        public CoopsBreakdown CoopsBreakdown { get; set; }
        public List<UserPreFarm> UserPreFarms { get; set; }
        public uint League { get; set; }
    }
}
