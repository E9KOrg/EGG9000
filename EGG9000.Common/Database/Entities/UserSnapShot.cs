using System;
using System.ComponentModel.DataAnnotations.Schema;

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

        public Ei.Egg CurrentEgg { get; set; }
        public double CuriosityDelivered { get; set; }
        public double IntegrityDelivered { get; set; }
        public double HumilityDelivered { get; set; }
        public double ResilienceDelivered { get; set; }
        public double KindnessDelivered { get; set; }
        public int TeTotal { get; set; }
        public uint TeEarned { get; set; }
        public int TePending { get; set; }
        public uint ShiftCount { get; set; }
        public uint Resets { get; set; }
    }
}
