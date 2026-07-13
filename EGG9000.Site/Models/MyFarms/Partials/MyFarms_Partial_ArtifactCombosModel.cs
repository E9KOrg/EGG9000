using EGG9000.Common.Database;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_ArtifactCombosModel(
        EggIncAccount account,
        int index,
        List<DBContract> Contracts,
        List<Coop> Coops,
        DBUser User,
        List<DBCustomEgg> CustomEggs
    ) : MyFarms_Partial_BaseModel(account, index) {
        public CustomBackup Backup => account.Backup;

        public MyFarms_Partial_ArtifactCombosModel(MyFarmsModel pageModel, int index) : this(
            pageModel.AccountAt(index),
            index,
            pageModel.Contracts,
            pageModel.JoinedCoops,
            pageModel.User,
            pageModel.CustomEggs
        ) { }
    }
}
