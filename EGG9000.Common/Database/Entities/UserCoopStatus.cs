using System;
using System.Collections.Generic;
using System.Text;

namespace DiscordCoopCodes.Database.Entities
{
    public class UserCoopStatus
    {
        public Guid Id { get; set; } 
        public Guid? UserId { get; set; }
        public Guid CoopId { get; set; }
        public DateTimeOffset CreatedOn { get; set; }

        public string EggIncId { get; set; }
        public string EggIncName { get; set; }

        public double Total { get; set; }
        public double Rate { get; set; }

        public DateTimeOffset? SleepingWarning { get; set; }

        public Coop Coop { get; set; }
        public DBUser User { get; set; }
    }
}
