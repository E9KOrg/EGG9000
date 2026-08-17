using EGG9000.Common.Database;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_ShipsAndFarmsModel(
        EggIncAccount account,
        int Index,
        List<Coop> JoinedCoops,
        List<DBContract> Contracts,
        List<DBCustomEgg> CustomEggs
    ) : MyFarms_Partial_BaseModel(account, Index) {
        public CustomBackup Backup => account.Backup;

        public MyFarms_Partial_ShipsAndFarmsModel(MyFarmsModel pageModel, int index) : this(
            pageModel.AccountAt(index),
            index,
            pageModel.JoinedCoops,
            pageModel.Contracts,
            pageModel.CustomEggs
        ) { }
    }
}
