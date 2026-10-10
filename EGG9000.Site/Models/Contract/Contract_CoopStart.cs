using System;
using System.Collections.Generic;

namespace EGG9000.Site.Models.Contract {
    public class Contract_CoopStart {
        public List<Contract_CoopUser> Users { get; set; }
    }

    public class Contract_CoopUser {
        public string EggIncId { get; set; }
        public Guid DatabaseId { get; set; }
        public uint Group { get; set; }
    }
}
