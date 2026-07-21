using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_FAQCustomizationModel {
        public List<FAQTopic> PalaceFAQTopics { get; set; }
        public List<FAQTopic> GuildFAQTopics { get; set; }
        public ulong PalaceGuildId { get; set; }
        public ulong GuildId { get; set; }
        public string GuildName { get; set; }
        public string UserDiscordUsername { get; set; }
        public ulong UserDiscordId { get; set; }
        public int KeywordMaxLength { get; set; }
    }
}
