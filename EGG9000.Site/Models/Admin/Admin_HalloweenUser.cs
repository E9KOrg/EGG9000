using Discord.Rest;

namespace EGG9000.Site.Models.Admin {
    public class Admin_HalloweenUser {
        public RestGuildUser User { get; set; }
        public bool NeedsProPermit { get; set; }
    }
}
