using System;

namespace EGG9000.Site.Models.Contract {
    public class Contract_RemoveXrefModel {
        public Guid UserId { get; set; }
        public Guid CoopId { get; set; }
        public string EggIncId { get; set; }
    }
}
