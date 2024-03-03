using MessagePack;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    public class UserCoopXref {
        public Guid UserId { get; set; }
        public Guid CoopId { get; set; }
        public string EggIncId { get; set; }
        public string RefEggIncId { get; set; }
        public string FixedUserName { get; set; }
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
        public bool TimeCheatReported { get; set; }

        public byte[] _lastStatusByte { get; set; }
        [NotMapped]
        private ContributionInfoCompact _lastStatus { get; set; }
        [NotMapped]
        public ContributionInfoCompact LastStatus {
            get {
                if(Status != null && Status != "null") {
                    var status = JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse.Types.ContributionInfo>(Status);
                    _lastStatus = new ContributionInfoCompact (status);
                    Status = null;
                }
                if(_lastStatus != null)
                    return _lastStatus;
                if(_lastStatusByte == null)
                    return null;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

                _lastStatus = MessagePackSerializer.Deserialize<ContributionInfoCompact>(_lastStatusByte, lz4Options);
                return _lastStatus;
            }
            set {
                _lastStatus = value;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _lastStatusByte = MessagePackSerializer.Serialize(value, lz4Options);
            }
        }

        public ulong SleepingDiscordMessageID { get; set; }
        public int HoursSleeping { get; set; }
        public float TotalHoursSleeping { get; set; }
        public float? SiloTimeHours { get; set; }

        public Coop Coop { get; set; }
        public DBUser User { get; set; }

        public bool NoDemerit { get; set; }
        public float? Score { get; set; }
        public float? RunningScore { get; set; }
        public double? SoulPower { get; set; }
        //public bool DemeritGivenForScore { get; set; }
        public Guid GetID() { return UserId; }

        public bool OutsideCoop { get; set; }
        public bool HasTachyonDeflector { get; set; }
        public bool EquipedTachyonDeflector { get; set; }
        public bool PingOnFull { get; set; }
        public bool PingOnHighestEB { get; set; }
        public bool PingOnFinished { get; set; }
        public bool CoopFullWarning { get; set; }
        public ulong Group { get; set; }
        public bool GussetCheatDetected { get; set; } = false;

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

        public byte[] _coopSettingByte { get; set; }
        [NotMapped]
        private CoopSetting _coopSetting { get; set; }
        [NotMapped]
        public CoopSetting CoopSetting {
            get {
                if(_coopSetting != null)
                    return _coopSetting;
                if(_coopSettingByte == null)
                    return null;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _coopSetting = MessagePackSerializer.Deserialize<CoopSetting>(_coopSettingByte, lz4Options);
                return _coopSetting;
            }
            set {
                _coopSetting = value;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _coopSettingByte = MessagePackSerializer.Serialize(value, lz4Options);
            }
        }

        public void UpdateCoopSetting() {
            CoopSetting = CoopSetting;
        }
    }
    [MessagePackObject]
    public class ContributionInfoCompact {
        [Key(0)]
        public double SoulPower { get; set; }
        [Key(1)]
        public double ContributionAmount { get; set; }
        [Key(2)]
        public uint BoostTokensSpent { get; set; }
        [Key(3)]
        public string UserName { get; set; }

        public ContributionInfoCompact() {

        }
        public ContributionInfoCompact(Ei.ContractCoopStatusResponse.Types.ContributionInfo info) {
            SoulPower = info.SoulPower;
            ContributionAmount = info.ContributionAmount;
            BoostTokensSpent = info.BoostTokensSpent;
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
        [Key(6)]
        public float Silos { get; set; }
        [Key(7)]
        public double EggsShipped { get; set; }
        [Key(8)]
        public double Rate { get; set; }
        [Key(9)]
        public double Expected { get; set; }
        [Key(10)]
        public double Actual { get; set; }
    }

    [MessagePackObject]
    public class CoopSetting {
        [Key(0)]
        public bool PingOnFull { get; set; }
        [Key(1)]
        public bool PingOnHighestEB { get; set; }
        [Key(2)]
        public bool PingOnFinished { get; set; }
        [Key(3)]
        public bool PingOnEveryoneCheckedIn { get; set; }
        [Key(4)]
        public bool PingOnMessage { get; set; }
        [Key(5)]
        public bool PingOnCoopCreated { get; set; }
        [Key(6)]
        public bool PingOnTachyonChange { get; set; }
        [Key(7)]
        public bool PingOnCompleteOnCheckIn { get; set; }

        [IgnoreMember]
        public bool this[string propertyName] {
            get {
                Type myType = typeof(CoopSetting);
                PropertyInfo myPropInfo = myType.GetProperty(propertyName);
                return (bool)myPropInfo.GetValue(this);
            }
            set {
                Type myType = typeof(CoopSetting);
                PropertyInfo myPropInfo = myType.GetProperty(propertyName);
                myPropInfo.SetValue(this, value);
            }
        }

        public CoopSetting() {

        }

        public CoopSetting(UserCoopXref xref, DBUser user, Guild userGuild) {
            if(user.CoopSetting is null)
                user.CoopSetting = new CoopSetting();

            PingOnFull = userGuild.IsLockedAndEnabled(GuildCoopSetting.PingOnFull) || user.CoopSetting.PingOnFull || xref.PingOnFull;
            PingOnHighestEB = userGuild.IsLockedAndEnabled(GuildCoopSetting.PingOnHighestEB) || user.CoopSetting.PingOnHighestEB || xref.PingOnHighestEB;
            PingOnFinished = userGuild.IsLockedAndEnabled(GuildCoopSetting.PingOnFinished) || user.CoopSetting.PingOnFinished;
            PingOnEveryoneCheckedIn = userGuild.IsLockedAndEnabled(GuildCoopSetting.PingOnEveryoneCheckedIn) ||  user.CoopSetting.PingOnEveryoneCheckedIn || xref.PingOnFinished;
            PingOnMessage = userGuild.IsLockedAndEnabled(GuildCoopSetting.PingOnMessage) ||  user.CoopSetting.PingOnMessage;
            PingOnCoopCreated = userGuild.IsLockedAndEnabled(GuildCoopSetting.PingOnCoopCreated) || user.CoopSetting.PingOnCoopCreated;
            PingOnTachyonChange = userGuild.IsLockedAndEnabled(GuildCoopSetting.PingOnTachyonChange) || user.CoopSetting.PingOnTachyonChange;
            PingOnCompleteOnCheckIn = userGuild.IsLockedAndEnabled(GuildCoopSetting.PingOnCompleteOnCheckIn) || user.CoopSetting.PingOnCompleteOnCheckIn;

            var disableOverrides = Enum.GetValues(typeof(GuildCoopSetting))
                .Cast<GuildCoopSetting>()
                .ToDictionary(setting => setting, userGuild.IsLockedAndDisabled);

            if(disableOverrides[GuildCoopSetting.PingOnFull]) PingOnFull = false;
            if(disableOverrides[GuildCoopSetting.PingOnHighestEB]) PingOnHighestEB = false;
            if(disableOverrides[GuildCoopSetting.PingOnFinished]) PingOnFinished = false;
            if(disableOverrides[GuildCoopSetting.PingOnEveryoneCheckedIn]) PingOnEveryoneCheckedIn = false;
            if(disableOverrides[GuildCoopSetting.PingOnMessage]) PingOnMessage = false;
            if(disableOverrides[GuildCoopSetting.PingOnCoopCreated]) PingOnCoopCreated = false;
            if(disableOverrides[GuildCoopSetting.PingOnTachyonChange]) PingOnTachyonChange = false;
            if(disableOverrides[GuildCoopSetting.PingOnCompleteOnCheckIn]) PingOnCompleteOnCheckIn = false;
        }
    }
}
