using MessagePack;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

using static EGG9000.Common.Database.Entities.DBUser;

namespace EGG9000.Common.Database.Entities {
    public class UpcomingContract {
        public Guid ID { get; set; }  //identifier

        public ulong GuildID { get; set; }
        public DateTimeOffset TargetDate { get; set; }
        public bool IsLeggacy { get; set; }
        public byte[] _userRegs { get; set; }
        public string ContractId { get; set; }
        public Contract Contract { get; set; }

        public ulong ChannelId { get; set; }
        [NotMapped]
        private List<UserRegister> _userRegisters { get; set; }
        [NotMapped]
        public List<UserRegister> UserRegisters {
            get {
                if(_userRegs is null)
                    _userRegisters = new List<UserRegister>();
                else
                    _userRegisters = MessagePackSerializer.Deserialize<List<UserRegister>>(_userRegs, lz4Options);
                return _userRegisters;
            }
            set {
                _userRegisters = value;
                _userRegs = MessagePackSerializer.Serialize(_userRegisters, lz4Options);
            }
        }

        [MessagePackObject]
        public class UserRegister {
            [Key(0)]
            public Guid UserID { get; set; }
            [Key(1)]
            public string EggIncId { get; set; }
            [Key(2)]
            public bool Skip { get; set; }
            [Key(3)]
            public byte Group { get; set; }
            [Key(4)]
            public List<Ei.RewardType> Rewards { get; set; }
        }
    }
}
