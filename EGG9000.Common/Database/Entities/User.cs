
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;

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

namespace DiscordCoopCodes.Database.Entities {
    [Table("Users")]
    public class DBUser {
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

        public List<Demerit> Demerits { get; set; }
        public List<Merit> Merits { get; set; }
        public List<Demerit> DemeritsGiven { get; set; }
        public List<Merit> MeritsGiven { get; set; }

        public byte[] _LastBackup { get; set; }
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
                    return null;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _backups = MessagePackSerializer.Deserialize<List<CustomBackup>>(_CustomBackups, lz4Options);
                return _backups;
            }
            set {
                _backups = value;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _CustomBackups = MessagePackSerializer.Serialize(value, lz4Options);
            }
        }

        [NotMapped]
        private List<ShipDM> _shipDMs{ get; set; }

        [NotMapped]
        public List<ShipDM> ShipDMs {
            get {
                if(_shipDMs != null)
                    return _shipDMs;
                if(_shipDMsByte == null)
                    return null;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _shipDMs = MessagePackSerializer.Deserialize<List<ShipDM>>(_shipDMsByte, lz4Options);
                return _shipDMs;
            }
            set {
                _shipDMs = value;
                var lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
                _shipDMsByte = MessagePackSerializer.Serialize(value, lz4Options);
                _shipDMsString = JsonConvert.SerializeObject(value);
            }
        }





        //public static void CopyTo(Stream src, Stream dest) {
        //    byte[] bytes = new byte[4096];

        //    int cnt;

        //    while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0) {
        //        dest.Write(bytes, 0, cnt);
        //    }
        //}

        [NotMapped]
        public List<EggIncNameAndId> EggIncIds {
            get => JsonConvert.DeserializeObject<List<EggIncNameAndId>>(_eggIncIds ?? "[]"); 
            set { _eggIncIds = JsonConvert.SerializeObject(value); Console.WriteLine("Updating _eggIncIds"); }

        }

        public DateTimeOffset CreateOn { get; set; }

        public List<UserCoopXref> UserCoopXrefs { get; set; }

        public bool UserMatchesProto(Ei.ContractCoopStatusResponse.Types.ContributionInfo proto) {
            return EggIncIds.Any(x => x.Id == proto.UserId || x.Name.ToLower() == proto.UserName.ToLower());
        }


        public void UpdateNameAndId(Ei.ContractCoopStatusResponse.Types.ContributionInfo proto) {
            var eggIncIds = JsonConvert.DeserializeObject<List<EggIncNameAndId>>(_eggIncIds ?? "[]");
            var nameId = eggIncIds.First(x => x.Id == proto.UserId || x.Name.ToLower() == proto.UserName.ToLower());

            var update = false;
            if (string.IsNullOrEmpty(nameId.Id)) {
                nameId.Id = proto.UserId;
                update = true;
                Console.WriteLine("Updating ID");
            }
            if (nameId.Name != proto.UserName) {
                nameId
                    .Name = proto.UserName;
                update = true;
                Console.WriteLine("Updating Name");
            }
            if (update) {
                EggIncIds = eggIncIds;//Force JSON Update
            }
        }



        public void AddName(string Name, string Id = null) {
            var eggIncIds = JsonConvert.DeserializeObject<List<EggIncNameAndId>>(_eggIncIds ?? "[]");
            eggIncIds.Add(new EggIncNameAndId { Name = Name, Id = Id });
            EggIncIds = eggIncIds; //Force JSON Update
        }

        public void RemoveName(string Name) {
            var eggIncIds = JsonConvert.DeserializeObject<List<EggIncNameAndId>>(_eggIncIds ?? "[]");
            eggIncIds.RemoveAll(x => x.Name == Name);
            EggIncIds = eggIncIds; //Force JSON Update
        }

        public void RemoveID(string id) {
            var eggIncIds = JsonConvert.DeserializeObject<List<EggIncNameAndId>>(_eggIncIds ?? "[]");
            eggIncIds.RemoveAll(x => x.Id.ToLower() == id.ToLower());
            EggIncIds = eggIncIds; //Force JSON Update
        }
    }

    public class EggIncNameAndId {
        public string Name { get; set; }
        public string Id { get; set; }
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
