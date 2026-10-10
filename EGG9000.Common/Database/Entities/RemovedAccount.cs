using System;

namespace EGG9000.Common.Database.Entities {
    public class RemovedAccount {
        public Guid UserId { get; set; }
        public string EggIncId { get; set; }
        public DateTimeOffset RemovedOn { get; set; }
        public ulong RemovedByDiscordId { get; set; }
        public int ReappliedCount { get; set; }
        public DateTimeOffset? LastReappliedOn { get; set; }
    }
}
