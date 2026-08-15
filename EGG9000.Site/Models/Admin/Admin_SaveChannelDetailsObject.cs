using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_SaveChannelDetailsObject {
        public List<ServerCoopSetting> CoopSettingsOverrides { get; set; }
        public List<ChannelDetail> ChannelDetails { get; set; }
        public string CoopCategories { get; set; }
        public string FinishedCategories { get; set; }
        public bool DisableBG { get; set; }
        public string GroupRoles { get; set; }
        public bool AllowGuilds { get; set; }
        public bool PublicScoreGrid { get; set; }
        public string CoopNamePrefix { get; set; }
        public bool RemoveFindCoopSpot { get; set; }
        public bool RemoveTestAssignment { get; set; }
        public bool AddOutsideCoops { get; set; }
        public bool FAQTopicsEnabled { get; set; }
        public int FAQTopicCooldownMinutes { get; set; }
        public float MinimumRunningScore { get; set; }
        public int OfflineDemeritHours { get; set; }
        public int OfflineWarningHours { get; set; }
        public int JoinTimeHours { get; set; }
        public int JoinTimeUltraHours { get; set; }
        public bool SiloRemindersEnabled { get; set; }
        public int SiloReminderFirstHours { get; set; }
        public int SiloReminderSecondHours { get; set; }
    }
}
