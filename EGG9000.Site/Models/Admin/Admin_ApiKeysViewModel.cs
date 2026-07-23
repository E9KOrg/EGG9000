using System;
using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_ApiKeysViewModel {
        public ulong GuildId { get; set; }
        public List<ApiKey> Keys { get; set; }
        public Dictionary<Guid, Admin_ApiKeyUsageSummary> KeyUsage { get; set; }
        // Non-null only immediately after creation - shown once to the admin, never stored.
        public string NewRawKey { get; set; }
        public int UnrecognizedAttempts7Days { get; set; }
    }

    public class Admin_ApiKeyUsageSummary {
        public int RequestsToday { get; set; }
        public int RequestsLast7Days { get; set; }
        public int UniqueIps7Days { get; set; }
        public bool IsSpike { get; set; }
    }
}
