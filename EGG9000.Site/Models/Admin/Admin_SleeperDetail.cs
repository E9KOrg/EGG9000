using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_SleeperDetail {
        public bool FreshEgg { get; set; }
        public List<Demerit> Demerits { get; set; }
        public string DiscordName { get; set; }
        public float CurrentSleep { get; set; }
        public float TotalCoopSleep { get; set; }
        public string CoopName { get; set; }
        public string ContractName { get; set; }
        public ulong DiscordChannelId { get; set; }
        public ulong GuildId { get; set; }
    }
}
