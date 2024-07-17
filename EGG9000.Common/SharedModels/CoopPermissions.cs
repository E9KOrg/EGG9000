using System.Collections.Generic;

namespace EGG9000.Common.SharedModels {
    public class CoopPermissions {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public List<ulong> UserIds { get; set; }
    }
}
