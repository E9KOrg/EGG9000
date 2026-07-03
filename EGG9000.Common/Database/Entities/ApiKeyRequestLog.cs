using System;

namespace EGG9000.Common.Database.Entities {
    public class ApiKeyRequestLog {
        public Guid Id { get; set; }

        // Null when the header's hash matched no stored key (brute-force / garbage key attempt).
        public Guid? ApiKeyId { get; set; }

        // Null whenever ApiKeyId is null - the guild isn't known until a key resolves.
        public ulong? GuildId { get; set; }

        public string IpAddress { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public bool Success { get; set; }
    }
}
