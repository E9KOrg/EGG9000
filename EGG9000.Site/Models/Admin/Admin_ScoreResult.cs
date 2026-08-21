using Discord;
using System.Collections.Generic;
using static Ei.Contract.Types;

namespace EGG9000.Site.Models.Admin {
    public class Admin_ScoreResult {
        public List<Admin_ScoreUser> UsersBelowThreshold { get; set; }
        public List<Admin_ScoreUser> TopScore { get; set; }
    }

    public class Admin_ScoreUser {
        public ulong DiscordId { get; set; }
        public string DiscordUsername { get; set; }
        public float RunningScore { get; set; }
        public float Score { get; set; }
        public IGuildUser DiscordUser { get; set; }
        public PlayerGrade Grade { get; set; }
        public string EggIncId { get; set; }
        public bool Disabled { get; set; }
    }
}
