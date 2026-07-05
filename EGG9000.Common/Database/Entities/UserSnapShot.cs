using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

using Newtonsoft.Json;

namespace EGG9000.Common.Database.Entities {
    public class UserSnapShot {
        [Column(TypeName = "Date")]
        public DateTime Date { get; set; }

        public Guid UserId { get; set; }

        public UInt64 EggsOfProphecy { get; set; }
        public double SoulEggs { get; set; }
        public double EarningsBonus { get; set; }
        public ulong Prestiges { get; set; }
        public string EggIncID { get; set; }
        public UInt64 EggsOfTruth { get; set; }

        public string VirtueStatsJson { get; set; } = "{}";

        [NotMapped]
        private VirtueSnapshotStats _virtueStats;
        [NotMapped]
        public VirtueSnapshotStats VirtueStats {
            get => _virtueStats ??= JsonConvert.DeserializeObject<VirtueSnapshotStats>(VirtueStatsJson ?? "{}") ?? new VirtueSnapshotStats();
            set {
                _virtueStats = value;
                VirtueStatsJson = JsonConvert.SerializeObject(value);
            }
        }
    }

    public class VirtueSnapshotStats : IEquatable<VirtueSnapshotStats> {
        public Ei.Egg CurrentEgg { get; set; }
        public Dictionary<Ei.Egg, double> Delivered { get; set; } = new();
        public int TeTotal { get; set; }
        public uint TeEarned { get; set; }
        public int TePending { get; set; }
        public uint ShiftCount { get; set; }
        public uint Resets { get; set; }

        public bool Equals(VirtueSnapshotStats other) =>
            other is not null
            && CurrentEgg == other.CurrentEgg
            && TeTotal == other.TeTotal
            && TeEarned == other.TeEarned
            && TePending == other.TePending
            && ShiftCount == other.ShiftCount
            && Resets == other.Resets
            && Delivered.OrderBy(kv => kv.Key).SequenceEqual(other.Delivered.OrderBy(kv => kv.Key));

        public override bool Equals(object obj) => Equals(obj as VirtueSnapshotStats);

        public override int GetHashCode() {
            var hash = new HashCode();
            hash.Add(CurrentEgg);
            hash.Add(TeTotal);
            hash.Add(TeEarned);
            hash.Add(TePending);
            hash.Add(ShiftCount);
            hash.Add(Resets);
            foreach (var kv in Delivered.OrderBy(kv => kv.Key)) {
                hash.Add(kv.Key);
                hash.Add(kv.Value);
            }
            return hash.ToHashCode();
        }
    }
}
