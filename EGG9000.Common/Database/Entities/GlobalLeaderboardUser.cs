using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    public class GlobalLeaderboardUser {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid id { get; set; }

        public bool NeedsUpdate { get; set; }
        public DateTimeOffset? LastUpdate { get; set; }
        public bool UpdateFailed { get; set; }

        public string EggIncId { get; set; }

        public string user_id { get; set; }
        public string user_name { get; set; }
        public DateTimeOffset LastBackup { get; set; }

        public UInt64 eggs_of_prophecy { get; set; }
        public double soul_eggs { get; set; }
        public double earnings_bonus { get; set; }
        public double lifetime_cash_earned { get; set; }
        public int DegreeOfSeperation { get; set; }
    }
}
