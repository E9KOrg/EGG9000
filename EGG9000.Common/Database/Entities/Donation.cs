using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordCoopCodes.Database.Entities {
    public class Donation {
        public Guid Id { get; set; }
        public DateTimeOffset When { get; set; }
        public Guid UserId { get; set; }
        public DBUser User { get; set; }
        public float Amount { get; set; }
        public string Type { get; set; }
    }

}
