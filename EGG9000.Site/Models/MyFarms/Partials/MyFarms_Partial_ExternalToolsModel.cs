using EGG9000.Common.Database;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_ExternalToolsModel(
        EggIncAccount account,
        int index,
        DBUser User
    ) : MyFarms_Partial_BaseModel(account, index) {
        public CustomBackup Backup => account.Backup;

        public MyFarms_Partial_ExternalToolsModel(MyFarmsModel pageModel, int index) : this(
            pageModel.AccountAt(index),
            index,
            pageModel.User
        ) { }
    }
}
