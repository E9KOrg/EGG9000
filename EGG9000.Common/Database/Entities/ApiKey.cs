using System;
using System.ComponentModel.DataAnnotations;

namespace EGG9000.Common.Database.Entities {
    public class ApiKey {
        public Guid Id { get; set; }

        // SHA-256 hex of the raw key (64 lowercase hex chars). Never store the raw key.
        [Required, MaxLength(64)]
        public string KeyHash { get; set; }

        // Human-readable label set by the admin who created the key.
        [Required, MaxLength(100)]
        public string Label { get; set; }

        public ulong GuildId { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        // Null = never expires.
        public DateTimeOffset? ExpiresAt { get; set; }

        public bool Revoked { get; set; }
    }
}
