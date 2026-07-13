using EGG9000.Common.Database;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_ColleggtiblesModel(
        EggIncAccount account,
        int index,
        List<DBCustomEgg> CustomEggs
    ) : MyFarms_Partial_BaseModel(account, index) {
        public CustomBackup Backup => account.Backup;

        public MyFarms_Partial_ColleggtiblesModel(MyFarmsModel pageModel, int index) : this(
            pageModel.AccountAt(index),
            index,
            pageModel.CustomEggs
        ) { }
    }
}
