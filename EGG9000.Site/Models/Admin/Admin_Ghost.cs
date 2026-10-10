using System;

namespace EGG9000.Site.Models.Admin {
    public class Admin_Ghost {
        public string Coop { get; set; }
        public ulong DiscordId { get; set; }
        public ulong CoopChannel { get; set; }
        public string ServerName { get; set; }
        public string UserName { get; set; }
        public bool Mentioned { get; set; }
        public DateTimeOffset? LastMention { get; set; }
        public bool MissingFromMain { get; set; }
        public Guid CoopId { get; set; }
        public Guid UserId { get; set; }
        public bool CoopFinished { get; set; }
    }
}
