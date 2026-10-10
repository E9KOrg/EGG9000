using MessagePack;
using System.Collections.Generic;
using static Ei.ArtifactSpec.Types;
using static Ei.MissionInfo.Types;

namespace EGG9000.Common.Database {
    [MessagePackObject]
    public class SpaceMission {
        [Key(0)]
        public Spaceship Ship { get; set; }
        [Key(1)]
        public DurationType Duration { get; set; }
        [Key(2)]
        public Status Status { get; set; }
        [Key(3)]
        public List<SpaceMissionFuel> Fuels { get; set; }
        [Key(4)]
        public long DurationSeconds { get; set; }
        [Key(5)]
        public long StartTime { get; set; }
        [Key(6)]
        public Name Targeting { get; set; } = Name.Unknown;
        [Key(7)]
        public uint Capacity { get; set; } = 0;
        [Key(8)]
        public uint Stars { get; set; } = 0;

        [IgnoreMember]
        public long ReturnTime {
            get {
                return StartTime + DurationSeconds;
            }
        }
    }

    [MessagePackObject]
    public class SpaceMissionFuel {
        [Key(0)]
        public Ei.Egg Egg { get; set; }
        [Key(1)]
        public double Amount { get; set; }
    }
}
