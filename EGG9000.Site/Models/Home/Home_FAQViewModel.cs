using System.Collections.Generic;
using EGG9000.Common.Database.Entities;

namespace EGG9000.Site.Models.Home {
    public class Home_FAQViewModel {
        public string GuildName { get; set; }
        public List<FAQTopic> FAQTopics { get; set; }
    }
}
