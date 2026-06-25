using System;

namespace EGG9000.Common.Database.Entities {
    // One row per account where the new (shadow) engine would have decided differently from the live
    // old logic, captured during a real assignment run. Diagnostics only: never read by assignment.
    // ExpectedSeasonalDeviation marks the approved seasonal-PE ruling changes so they can be filtered
    // out when hunting real regressions.
    public class ShadowAssignmentDiff {
        public Guid Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string ContractId { get; set; }
        public ulong GuildId { get; set; }
        public string EggIncId { get; set; }
        public ulong DiscordId { get; set; }
        public bool LiveAssigned { get; set; }
        public bool ShadowAssigned { get; set; }
        public string LiveReason { get; set; }
        public string ShadowReason { get; set; }
        public bool ExpectedSeasonalDeviation { get; set; }
    }
}
