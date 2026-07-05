using System;

namespace EGG9000.Common.Database.Entities {
    public class ApiKeyDailyUsage {
        public Guid ApiKeyId { get; set; }

        // Calendar day (UTC), time component always midnight - matches UserSnapShot.Date convention.
        public DateTime Date { get; set; }

        public int RequestCount { get; set; }
    }
}
