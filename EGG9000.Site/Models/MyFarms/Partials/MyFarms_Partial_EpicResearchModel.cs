using EGG9000.Common.Database;
using EGG9000.Common.JsonData;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_EpicResearchModel(
        EggIncAccount account,
        int index,
        List<EpicResearchItem> epicResearchConfig
    ) : MyFarms_Partial_BaseModel(account, index) {
        public CustomBackup backup => account.Backup;

        public MyFarms_Partial_EpicResearchModel(MyFarmsModel pageModel, int index) : this(
            pageModel.AccountAt(index),
            index,
            pageModel.EpicResearchConfig
        ) { }
    }
}
