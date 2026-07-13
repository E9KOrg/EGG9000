using EGG9000.Common.Database;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_VirtueStatsModel(
        EggIncAccount account,
        int index,
        List<UserSnapShot> snapshots,
        bool isAdmin
    ) : MyFarms_Partial_BaseModel(account, index) {
        public CustomBackup backup => account.Backup;

        public MyFarms_Partial_VirtueStatsModel(MyFarmsModel pageModel, int index, bool isAdmin) : this(
            pageModel.AccountAt(index),
            index,
            pageModel.SnapShots,
            isAdmin
        ) { }
    }
}
