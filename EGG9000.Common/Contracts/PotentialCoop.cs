using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static EGG9000.Common.Database.Entities.DBUser;

namespace EGG9000.Common.Contracts {
    public class PotentialCoopGroup {
        public Ei.Contract.Types.PlayerGrade Grade { get; set; }
        public int BoardingGroup { get; set; }
        public string bg { get { return BoardingGroup.ToString();  } }

        public List<PotentialCoop> PotentialCoops { get; set; }
    }
    public class PotentialCoop {
        public List<UserByAccount> Users { get; set; }
    }

    public class UserByAccount {
        public DBUser User { get; set; }
        public CustomBackup Backup { get; set; }
        public EggIncAccount AccountSettings { get; set; }
    }
}
