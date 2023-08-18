
using Bugsnag.Payload;

using EGG9000.Common.Database;
using EGG9000.Common.Helpers;

using Ei;

using Google.Protobuf.WellKnownTypes;

using MessagePack;

using Microsoft.AspNetCore.Identity;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    [Table("Users")]
    public class DBUser {
        [NotMapped]
        public static readonly MessagePackSerializerOptions lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        public Guid Id { get; set; }
        public ulong DiscordId { get; set; }
        public string DiscordUsername { get; set; }
        //public string EggIncNames { get; set; }
        public string _eggIncIds { get; set; }
        public DateTimeOffset LastSleepingNotification { get; set; }
        public ushort GuildCoops { get; set; }
        public ulong GuildId { get; set; }
        public ulong? LastGuild { get; set; }

        public bool AcceptedRules { get; set; }

        public bool TempDisabled { get; set; }
        public bool showEB { get; set; }

        public DateTimeOffset? OnBreakSince { get; set; }
        public bool SkipNoPE { get; set; }
        public bool SkipNoArtifacts { get; set; }
        public bool SkipNoPiggyDouble { get; set; }
        [NotMapped]
        public List<Ei.RewardType> PingForRewards {
            get {
                if(!SkipNoArtifacts && !SkipNoPE && !SkipNoPiggyDouble) {
                    return new List<Ei.RewardType> { Ei.RewardType.UnknownReward };
                }
                var rewards = new List<Ei.RewardType>();
                if(SkipNoPE)
                    rewards.Add(Ei.RewardType.EggsOfProphecy);
                if(SkipNoArtifacts) {
                    rewards.Add(Ei.RewardType.Artifact);
                    rewards.Add(Ei.RewardType.ArtifactCase);
                }
                if(SkipNoPiggyDouble)
                    rewards.Add(Ei.RewardType.PiggyMultiplier);
                return rewards;
            }
        }

        public List<Demerit> Demerits { get; set; }
        public List<Merit> Merits { get; set; }
        public List<Demerit> DemeritsGiven { get; set; }
        public List<Merit> MeritsGiven { get; set; }

        public string CustomCoopName { get; set; }
        public DateTimeOffset? ExpireCustomCoopName { get; set; }

        //public byte[] _LastBackup { get; set; }
        public byte[] _CustomBackups { get; set; }

        public bool DMOnShipReturn { get; set; }
        public int ShipReturnMinutes { get; set; }
        public int ShipReturnStillFuelingMinutes { get; set; }
        public bool ShipReturnDMAfterFuel { get; set; }
        public DateTimeOffset? NextShipReturnDMDue { get; set; }
        public byte[] _shipDMsByte { get; set; }
        public string Notes { get; set; }
        public byte[] _coopSettingByte { get; set; }
        public DateTimeOffset? NextBreakExpire { get; set; }
        [NotMapped]
        private CoopSetting _coopSetting { get; set; }
        [NotMapped]
        public CoopSetting CoopSetting {
            get {
                if(_coopSetting != null)
                    return _coopSetting;
                if(_coopSettingByte == null)
                    return null;
                _coopSetting = MessagePackSerializer.Deserialize<CoopSetting>(_coopSettingByte, lz4Options);
                return _coopSetting;
            }
            set {
                _coopSetting = value;
                _coopSettingByte = MessagePackSerializer.Serialize(value, lz4Options);
            }
        }
        public bool Banned { get; set; } = false;


        [NotMapped]
        private List<CustomBackup> _backups { get; set; }

        [NotMapped]
        private List<CustomBackup> Backups {
            get {
                return EggIncAccounts.Select(x => x.Backup).ToList();
                //if(_backups != null)
                //    return _backups;
                //if(_CustomBackups == null)
                //    return new List<CustomBackup>();
                //_backups = MessagePackSerializer.Deserialize<List<CustomBackup>>(_CustomBackups, lz4Options);
                //return _backups;
            }
            //set {
            //    foreach(var account in EggIncAccounts) {
            //        var backup = value.FirstOrDefault(x => x.EggIncId == account.Id);
            //        if(backup is not null && backup.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset && backup.Grade != account.LastGrade) {
            //            var backupTime = DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime);
            //            if(backupTime > account.PromotionTime) {
            //                account.LastGrade = backup.Grade;
            //                UpdateAccounts();
            //            }
            //        }
            //    }
            //    _backups = value;
            //    _CustomBackups = MessagePackSerializer.Serialize(value, lz4Options);
            //}
        }

        //public Ei.Contract.Types.PlayerGrade GetGrade(string EIID) {
        //    var backup = Backups.FirstOrDefault(x => x.EggIncId == EIID);
        //    var account = EggIncAccounts.FirstOrDefault(x => x.Id == EIID);
        //    if(backup != null && account.PromotionTime > backup.GetLastBackupDateTime())
        //        return account.LastGrade;
        //    if(backup is not null && backup.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset)
        //        return backup.Grade;
        //    if(account is not null && account.LastGrade != Ei.Contract.Types.PlayerGrade.GradeUnset)
        //        return account.LastGrade;

        //    if(backup is not null) {
        //        var farms = new List<(float, Ei.Contract.Types.PlayerGrade)>();
        //        farms.AddRange(backup.Farms.Where(x => x.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset).Select(x => ((float)x.TimeAccepted, x.Grade)));
        //        farms.AddRange(backup.ArchivedFarms.Where(x => x.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset).Select(x => ((float)x.TimeAccepted, x.Grade)));
        //        var latestFarms = farms.OrderByDescending(x => x.Item1).ToList();
        //        if(latestFarms.Count > 0) {
        //            return latestFarms.First().Item2;
        //        }
        //    }
        //    return Ei.Contract.Types.PlayerGrade.GradeUnset;
        //}

        [NotMapped]
        private List<ShipDM> _shipDMs { get; set; }

        [NotMapped]
        public List<ShipDM> ShipDMs {
            get {
                if(_shipDMs != null)
                    return _shipDMs;
                if(_shipDMsByte == null)
                    return null;
                _shipDMs = MessagePackSerializer.Deserialize<List<ShipDM>>(_shipDMsByte, lz4Options);
                return _shipDMs;
            }
            set {
                _shipDMs = value;
                _shipDMsByte = MessagePackSerializer.Serialize(value, lz4Options);
            }
        }

        public byte[] _contractRegistrationByte { get; set; }

        [NotMapped]
        private List<EggIncAccount> _accounts = null;
        [NotMapped]
        public List<EggIncAccount> EggIncAccounts {
            get {
                if(_contractRegistrationByte is null) {
                    _accounts = JsonConvert.DeserializeObject<List<EggIncAccount>>(_eggIncIds ?? "[]");
                } else if(_accounts is not null) {
                    return _accounts;
                } else {
                    _accounts = MessagePackSerializer.Deserialize<List<EggIncAccount>>(_contractRegistrationByte, lz4Options);
                    bool needsUpdate = false;
                    if(_accounts is null) {
                        _accounts = new List<EggIncAccount>();
                        needsUpdate = true;
                    }

                    if(_CustomBackups is not null && _CustomBackups.Length > 0) {
                        var backups = MessagePackSerializer.Deserialize<List<CustomBackup>>(_CustomBackups, lz4Options);
                        _accounts.ForEach(x => {
                            x.Backup = backups.FirstOrDefault(y => y.EggIncId == x.Id);
                        });
                        _CustomBackups = new byte[0];
                        needsUpdate = true;
                    }

                    _accounts.ForEach(account => {
                        if(account.RedoLeggacySelection == RedoLeggacyOption.NotSet)
                            account.RedoLeggacySelection = account.RedoLeggacy ? RedoLeggacyOption.YesAll : RedoLeggacyOption.No;
                        if(account.Backup is not null && account.Backup.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset && account.Backup.Grade != account.LastGrade) {
                            var backupTime = DateTimeOffset.FromUnixTimeSeconds(account.Backup.LastBackupTime);
                            if(backupTime > account.PromotionTime) {
                                account.LastGrade = account.Backup.Grade;
                                needsUpdate = true;
                            }
                        }
                        //Sync account's Device ID from backup
                        if(account.Backup is not null && account.Backup.HasDeviceId && (account.DeviceID == "" || account.DeviceID != account.Backup.DeviceId)) {
                            account.DeviceID = account.Backup.DeviceId;
                        }
                    });
                    if(needsUpdate) {
                        UpdateAccounts();
                    }
                }
                return _accounts;
            } set {
                if(value == null) {
                    Console.WriteLine("Trying to save NULL EggIncAccounts");
                } else {
                    _accounts = value;
                    UpdateAccounts();
                }
            }

        }

        public void UpdateAccounts() {
            if(_eggIncIds is not null)
                _eggIncIds = null;
            if(_accounts is not null)
                _contractRegistrationByte = MessagePackSerializer.Serialize(_accounts, lz4Options);
        }

        public DateTimeOffset CreateOn { get; set; }
        public DateTimeOffset? Registered { get; set; }

        public List<UserCoopXref> UserCoopXrefs { get; set; }

        public bool UserMatchesProto(Ei.ContractCoopStatusResponse.Types.ContributionInfo proto) {
            return EggIncAccounts.Any(x => x.Id == proto.UserId);
        }


        public void UpdateNameAndId(Ei.ContractCoopStatusResponse.Types.ContributionInfo proto) {
            var eggIncIds = EggIncAccounts;
            var nameId = eggIncIds.First(x => x.Id == proto.UserId);

            var update = false;
            if(string.IsNullOrEmpty(nameId.Id)) {
                nameId.Id = proto.UserId;
                update = true;
            }
            if(update) {
                UpdateAccounts();//Force JSON Update
            }
        }



        public void AddName(string Name, CustomBackup backup, string Id = null) {
            var eggIncIds = EggIncAccounts;
            eggIncIds.Add(new EggIncAccount { Id = Id, Backup = backup });
            UpdateAccounts();//Force JSON Update
        }

        public void RemoveID(string id) {
            var eggIncIds = EggIncAccounts;
            eggIncIds.RemoveAll(x => x.Id.ToLower() == id.ToLower());
            UpdateAccounts();//Force JSON Update
        }



        [MessagePackObject]
        public class ShipDM {
            [Key(0)]
            public string EggIncID { get; set; }
            [Key(1)]
            public DateTimeOffset DMTime { get; set; }
            [Key(2)]
            public bool Sent { get; set; }
            [Key(3)]
            public long ShipReturnTime { get; set; }
        }

        public static bool MatchRewards(Ei.Contract.Types.GradeSpec gradeSpec, Ei.RewardType selectReward) {
            switch(selectReward) {
                case Ei.RewardType.Artifact:
                    return gradeSpec.Goals.Any(g => g.RewardType == Ei.RewardType.Artifact || g.RewardType == Ei.RewardType.ArtifactCase);
                case Ei.RewardType.PiggyMultiplier:
                    return gradeSpec.Goals.Any(g => g.RewardType == Ei.RewardType.PiggyMultiplier || g.RewardType == Ei.RewardType.PiggyLevelBump || g.RewardType == RewardType.PiggyFill);
                default:
                    return gradeSpec.Goals.Any(g => g.RewardType == selectReward);
            }
        }

        public void UpdateUserBreak() {
            var accountsWithExpire = this.EggIncAccounts.Where(x => x.OnBreakUntil != default && !x.SentBreakWarning && x.OnBreakUntil > DateTimeOffset.Now).ToList();

            if(accountsWithExpire.Count == 0) {
                this.NextBreakExpire = null;
            } else if(this.EggIncAccounts.Count > 0) {
                this.NextBreakExpire = accountsWithExpire.Min(x => x.OnBreakUntil);
            }

        }
    }

    [MessagePackObject]
    public class EggIncAccount {
        [Key(1)]
        public string Id { get; set; }
        [Key(2)]
        public DateTimeOffset OnBreakUntil { get; set; }
        [Key(3)]
        public List<Ei.RewardType> AutoRegisterRewards { get; set; }
        [Key(4)]
        public bool bool1 { get; set; } //Not being used
        [Key(5)]
        public byte Group { get; set; }
        [Key(6)]
        public bool bool2 { get; set; } //Not being user
        [Key(7)]
        public bool RedoLeggacy { get; set; }
        [Key(8)]
        public Ei.Contract.Types.PlayerGrade LastGrade { get; set; }
        [Key(9)]
        public DateTimeOffset PromotionTime { get; set; }
        [Key(10)]
        public CustomBackup Backup { get; set; }
        [Key(11)]
        public int RedoScoreThreshold { get; set; } = 20000;
        [Key(12)]
        public RedoLeggacyOption RedoLeggacySelection { get; set; } = RedoLeggacyOption.NotSet;
        [Key(13)]
        public bool Active { get; set; }
        [Key(14)]
        public DateTimeOffset? LastEBTime { get; set; }
        [Key(15)]
        public double LastEB { get; set; }
        [Key(16)]
        public bool SentBreakWarning { get; set; }
        [Key(17)]
        public string Guild { get; set; }
        [Key(18)]
        public string DeviceID { get; set; } = string.Empty;
        [Key(19)]
        public double SubscriptionEnds { get; set; }
        [Key(20)]
        public Ei.UserSubscriptionInfo.Types.Level? SubscriptionLevel { get; set; }
        [Key(21)]
        public bool AFSWarningSent { get; set; } = false;
        [Key(22)]
        public bool AFSMarkedClean { get; set; } = false;
        [Key(23)]
        public byte UltraGroup { get; set; }
        [Key(25)]
        public List<Ei.RewardType> LeggacyAutoRegisterRewards { get; set; }

        public byte GetGroup(bool Ultra) {
            if(Ultra && UltraGroup > 0)
                return UltraGroup;
            return Group;
        }

        public void SetBreak(DateTimeOffset until, DBUser dbuser) {
            OnBreakUntil = until;
            SentBreakWarning = false;

            dbuser.UpdateUserBreak();
        }

        public void BreakWarningSent(DBUser dbuser) {
            SentBreakWarning = true;
            dbuser.UpdateUserBreak();

        }


        public Ei.Contract.Types.PlayerGrade GetGrade() {
            if(Backup != null && PromotionTime > Backup.GetLastBackupDateTime())
                return LastGrade;
            if(Backup is not null && Backup.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset)
                return Backup.Grade;
            if(LastGrade != Ei.Contract.Types.PlayerGrade.GradeUnset)
                return LastGrade;

            if(Backup is not null) {
                var farms = new List<(float, Ei.Contract.Types.PlayerGrade)>();
                farms.AddRange(Backup.Farms.Where(x => x.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset).Select(x => ((float)x.TimeAccepted, x.Grade)));
                farms.AddRange(Backup.ArchivedFarms.Where(x => x.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset).Select(x => ((float)x.TimeAccepted, x.Grade)));
                var latestFarms = farms.OrderByDescending(x => x.Item1).ToList();
                if(latestFarms.Count > 0) {
                    return latestFarms.First().Item2;
                }
            }
            return Ei.Contract.Types.PlayerGrade.GradeUnset;

        }

    }
}
