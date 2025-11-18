using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
    public class GuildContract {
        public string ContractID { get; set; }
        public ulong GuildID { get; set; }

        public Contract Contract { get; set; }

        public ulong DiscordChannelId { get; set; }
        public DateTimeOffset? WarningForDeleteChannel { get; set; }
        public bool DeletedChannel { get; set; }

        public int NumberOfCoops { get; set; }
        public string Starters { get; set; }
        public string Skip { get; set; }
        public ContractStatus Status { get; set; }

        [Column] 
        private bool Elite { get; set; }
        public bool HasScores { get; set; }

        public string OutsideCoops { get; set; }
        public UInt32 League { get; set; }
        public int BoardingGroup { get; set; }
        public bool CcOnly { get; set; }
        public bool ReadyToScore { get; set; }

        //[NotMapped]
        //public List<Guid> StartersList { 
        //    get {
        //       return JsonConvert.DeserializeObject<List<Guid>>(Starters ??"[]");
        //    } 
        //}

        public DateTimeOffset Created { get; set; }
    }

    public enum ContractStatus {
        Prefarming = 1,
        Creating = 2,
        Completed = 3
    }
}
