using EGG9000.Common.Database;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_ContractHistoryModel(
        EggIncAccount account,
        int index,
        bool isAdmin,
        List<DBContract> contracts,
        List<DBCustomEgg> CustomEggs,
        DBUser User,
        Ei.MyContracts scores
    ) : MyFarms_Partial_BaseModel(account, index) {
        public CustomBackup backup => account.Backup;

        public MyFarms_Partial_ContractHistoryModel(MyFarmsModel pageModel, int index, bool isAdmin) : this(
            pageModel.AccountAt(index),
            index,
            isAdmin,
            pageModel.Contracts,
            pageModel.CustomEggs,
            pageModel.User,
            pageModel.Scoring.FirstOrDefault(x => x.EggIncId == pageModel.AccountAt(index).Backup.EggIncId).MyContracts
        ) { }
    }
}
