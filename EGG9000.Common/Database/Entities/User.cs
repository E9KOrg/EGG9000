
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;

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
        public DateTimeOffset? NextShipReturnDMDue { get; set; }
        public byte[] _shipDMsByte { get; set; }
        public string _shipDMsString { get; set; }

        [NotMapped]
        private List<CustomBackup> _backups { get; set; }

        [NotMapped]
        public List<CustomBackup> Backups {
            get {
                if(_backups != null)
                    return _backups;
                if(_CustomBackups == null)
                    return new List<CustomBackup>();
                _backups = MessagePackSerializer.Deserialize<List<CustomBackup>>(_CustomBackups, lz4Options);
                return _backups;
            }
            set {
                _backups = value;
                _CustomBackups = MessagePackSerializer.Serialize(value, lz4Options);
            }
        }

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
                _shipDMsString = JsonConvert.SerializeObject(value);
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
                } else {
                    _accounts = MessagePackSerializer.Deserialize<List<EggIncAccount>>(_contractRegistrationByte, lz4Options);
                }
                return _accounts;
            } set {
                _accounts = value;
                UpdateAccounts();
            }

        }

        public void UpdateAccounts() {
            if(_eggIncIds is not null)
                _eggIncIds = null;
            _contractRegistrationByte = MessagePackSerializer.Serialize(_accounts, lz4Options);
        }

        public DateTimeOffset CreateOn { get; set; }
        public DateTimeOffset? Registered { get; set; }

        public List<UserCoopXref> UserCoopXrefs { get; set; }

        public bool UserMatchesProto(Ei.ContractCoopStatusResponse.Types.ContributionInfo proto) {
            return EggIncAccounts.Any(x => x.Id == proto.UserId || x.Name.ToLower() == proto.UserName.ToLower());
        }


        public void UpdateNameAndId(Ei.ContractCoopStatusResponse.Types.ContributionInfo proto) {
            var eggIncIds = EggIncAccounts;
            var nameId = eggIncIds.First(x => x.Id == proto.UserId || x.Name.ToLower() == proto.UserName.ToLower());

            var update = false;
            if(string.IsNullOrEmpty(nameId.Id)) {
                nameId.Id = proto.UserId;
                update = true;
                Console.WriteLine("Updating ID");
            }
            if(nameId.Name != proto.UserName) {
                nameId
                    .Name = proto.UserName;
                update = true;
                Console.WriteLine("Updating Name");
            }
            if(update) {
                UpdateAccounts();//Force JSON Update
            }
        }



        public void AddName(string Name, string Id = null) {
            var eggIncIds = EggIncAccounts;
            eggIncIds.Add(new EggIncAccount { Name = Name, Id = Id });
            UpdateAccounts();//Force JSON Update
        }

        public void RemoveID(string id) {
            var eggIncIds = EggIncAccounts;
            eggIncIds.RemoveAll(x => x.Id.ToLower() == id.ToLower());
            UpdateAccounts();//Force JSON Update
        }

        [MessagePackObject]
        public class EggIncAccount {
            [Key(0)]
            public string Name { get; set; }
            [Key(1)]
            public string Id { get; set; }
            [Key(2)]
            public DateTimeOffset OnBreakUntil { get; set; }
            [Key(3)]
            public List<Ei.RewardType> AutoRegisterRewards { get; set; }
            [Key(4)]
            public bool AutoRegister { get; set; } //Not being used
            [Key(5)]
            public byte Group { get; set; }
            [Key(6)]
            public bool EnableFilter { get; set; }
            [Key(7)]
            public bool RedoLeggacy { get; set; }
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
    }
}
