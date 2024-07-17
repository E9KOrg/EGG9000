using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
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
