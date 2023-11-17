using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.SharedModels {
    public class CoopPermissions {
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public List<ulong> UserIds { get; set; }
    }
}
