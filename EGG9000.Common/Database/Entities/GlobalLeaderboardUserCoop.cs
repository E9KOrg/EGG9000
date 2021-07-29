using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text;

namespace DiscordCoopCodes.Database.Entities {
    public class GlobalLeaderboardCoop {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid id { get; set; }

        public string Name { get; set; }
        public string ContractID { get; set; }
        public bool Checked { get; set; }
        public bool CheckFailed { get; set; }
        public int DegreeOfSeperation { get; set; }
    }
}
