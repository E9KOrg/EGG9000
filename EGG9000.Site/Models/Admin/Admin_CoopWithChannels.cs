using Discord.WebSocket;
using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_CoopWithChannels {
        public Coop Coop { get; set; }
        public SocketTextChannel MainChannel { get; set; }
        public List<SocketTextChannel> ExtraChannels { get; set; }
    }
}
