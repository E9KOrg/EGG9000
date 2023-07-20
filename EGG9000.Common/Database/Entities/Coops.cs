using EGG9000.Common.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    public class Coop {
        public Guid Id { get; set; }
        public string ContractID { get; set; }
        public string Name { get; set; }

        public int? CurrentUsers { get; set; }
        public int? MaxUsers { get; set; }
        public int JoinUsers { get; set; }

        public DateTimeOffset? CoopEnds { get; set; }
        public DateTimeOffset? CoopCompleted { get; set; }

        public DateTimeOffset Created { get; set; }

        public bool ProjectedToFinish { get; set; }
        public bool Finished { get; set; }
        public UInt32 League { get; set; }
        public bool AnyLeague { get; set; } 

        public ulong DiscordChannelId { get; set; }
        public ulong GuildId { get; set; }
        public ulong OverflowGuildId { get; set; }
        public string UpdateMessagesId { get; set; }

        public string CreatorID { get; set; }
        public DateTimeOffset? LastUpdateToChannel { get; set; }
        public DateTimeOffset? WarningForDeleteChannel {get; set;}
        public bool DeletedChannel { get; set; }
        public ulong Group { get; set; }

        public CoopStatusEnum Status { get; set; }
        //public int UnableToFind { get; set; }

        public Contract Contract { get; set; }
        //public List<CoopStatus> CoopStatuses { get; set; }
        public List<UserCoopXref> UserCoopsXrefs { get; set; }

        public byte[] _StatusCompressed { get; set; }

        [NotMapped]
        private Ei.ContractCoopStatusResponse _status { get; set; }

        [NotMapped]
        public Ei.ContractCoopStatusResponse LastStatusUpdate {
            get {
                if (_status != null)
                    return _status;
                if (_StatusCompressed == null)
                    return null;
                using (var msi = new MemoryStream(_StatusCompressed))
                using (var mso = new MemoryStream()) {
                    using (var gs = new GZipStream(msi, CompressionMode.Decompress)) {
                        //gs.CopyTo(mso);
                        CopyTo(gs, mso);
                    }

                    _status = JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse>(Encoding.UTF8.GetString(mso.ToArray()));
                    return _status;
                }
            }
            set {
                _status = value;
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, new JsonSerializerSettings { ContractResolver = new CustomContractResolver() }));

                using (var msi = new MemoryStream(bytes))
                using (var mso = new MemoryStream()) {
                    using (var gs = new GZipStream(mso, CompressionMode.Compress)) {
                        //msi.CopyTo(gs);
                        CopyTo(msi, gs);
                    }

                    _StatusCompressed = mso.ToArray();
                }
            }
        }

        public static void CopyTo(Stream src, Stream dest) {
            byte[] bytes = new byte[4096];

            int cnt;

            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0) {
                dest.Write(bytes, 0, cnt);
            }
        }

        [NotMapped]
        public bool FinishedOrFailed {
            get {
                return Status == CoopStatusEnum.Completed || Status == CoopStatusEnum.Failed;
            }
        }

        [NotMapped]
        public bool FinishedOrFailedOrExpired {
            get {
                return Status == CoopStatusEnum.Completed || Status == CoopStatusEnum.Failed || CoopEnds < DateTimeOffset.Now;
            }
        }
    }

    public enum CoopStatusEnum {
        ManualWaitingOnCreation = 1,
        ManualCreated = 2,
        WaitingOnStarter = 10,
        WaitingOnAssigned = 11,
        AllAssignedJoined = 12,
        Full = 13,
        Completed = 14,
        Failed = -1
    }
}
