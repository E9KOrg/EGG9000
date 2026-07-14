using System;
using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_Slacker {
        public string DiscordUsername { get; set; }
        public bool Standard { get; set; }
        public int AccountCount { get; set; }
        public IEnumerable<Admin_SlackerXref> UserCoopXrefs { get; set; }
        public Guid Id { get; set; }
    }

    public class Admin_SlackerXref {
        public float? Score { get; set; }
        public string ContractID { get; set; }
        public float? RunningScore { get; set; }
        public DateTimeOffset Date { get; set; }
    }
}
