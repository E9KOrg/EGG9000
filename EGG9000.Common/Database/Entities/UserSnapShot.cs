using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    public class UserSnapShot {
        [Column(TypeName = "Date")]
        public DateTime Date { get; set; }

        public Guid UserId { get; set; }

        public UInt64 EggsOfProphecy { get; set; }
        public double SoulEggs { get; set; }
        public double EarningsBonus { get; set; }
        public ulong Prestiges { get; set; }
        public string EggIncID { get; set; }
    }
}
