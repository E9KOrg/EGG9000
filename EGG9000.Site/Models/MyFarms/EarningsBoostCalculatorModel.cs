using EGG9000.Common.Database;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public class MyFarms_Partial_EBCalcModel {
        public CustomBackup Backup { get; set; }
        public DBEvent Event { get; set; }
        public List<DBCustomEgg> CustomEggs { get; set; }
    }
}
