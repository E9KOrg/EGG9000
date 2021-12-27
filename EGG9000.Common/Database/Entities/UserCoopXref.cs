using MessagePack;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace EGG9000.Common.Database.Entities
{
    public class UserCoopXref
    {
        public Guid UserId { get; set; }
        public Guid CoopId { get; set; }
        public string EggIncId { get; set; }
        public string RefEggIncId { get; set; }
        public DateTimeOffset CreatedOn { get; set; }
        public bool JoinedCoop { get; set; }
        public bool WaitingOnStarter { get; set; }
        public bool AddedToChannel { get; set; }
        public bool Starter { get; set; }
        public bool WasAssigned { get; set; }

        public bool JoinWarning12h { get; set; }
        public bool JoinWarning24h { get; set; }
        public bool JoinWarning24TillFinish { get; set; }

        public DateTimeOffset? LastStatusTime { get; set; }
        public DateTimeOffset? SleepingWarningTime { get; set; }
        public string Status { get; set; }

        public ulong SleepingDiscordMessageID { get; set; }
        public int HoursSleeping { get; set; }
        public float TotalHoursSleeping { get; set; }
        public float? SiloTimeHours { get; set; }

        public Coop Coop { get; set; }
        public DBUser User { get; set; }

        public bool NoDemerit { get; set; }
        public bool PingOnFull { get; set; }
        public Guid GetID() { return UserId; }

        public byte[] _sleepTrackingByte { get; set; }
        [NotMapped]
        private List<SleepTracking> _sleepTracking { get; set; }
        [NotMapped]
        public List<SleepTracking> SleepTracking {
            get {
                if(_sleepTracking != null)
                    return _sleepTracking;
                if(_sleepTrackingByte == null) {
                    _sleepTracking = new List<SleepTracking>();
                    return _sleepTracking;
                }
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _sleepTracking = MessagePackSerializer.Deserialize<List<SleepTracking>>(_sleepTrackingByte, lz4Options);
                return _sleepTracking;
            }
            set {
                _sleepTracking = value;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _sleepTrackingByte = MessagePackSerializer.Serialize(value, lz4Options);
            }
        }
    }
    [MessagePackObject]
    public class SleepTracking {
        [Key(0)]
        public DateTimeOffset SleepStart { get; set; }
        [Key(1)]
        public int DemeritsGiven { get; set; }
        [Key(2)]
        public float TotalHoursEmpty { get; set; }
        [Key(3)]
        public double LostEarnings { get; set; }
        [Key(4)]
        public bool WokeUp { get; set; }
        [Key(5)]
        public DateTimeOffset LastChecked { get; set; }
    }
}
