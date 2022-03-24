using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    public class TemporaryRole {
        public ulong UserId { get; set; }
        public ulong GuildId { get; set; }
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset Expires { get; set; }
        public ulong RoleId { get; set; }
        public string Reason { get; set; }

        public bool IsRemoved { get; set; }
    }

}
