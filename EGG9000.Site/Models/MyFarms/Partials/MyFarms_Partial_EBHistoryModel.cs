using EGG9000.Common.Database;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_EBHistoryModel(
        EggIncAccount account,
        int index,
        List<UserSnapShot> snapshots,
        bool isAdmin,
        Dictionary<string, List<DBContract>> uncompletePEs,
        List<DBContract> contracts,
        (int Earned, int Max) peFromSeasons,
        List<MissingSeasonalPe> missingSeasonalPEs
    ) : MyFarms_Partial_BaseModel(account, index) {
        public CustomBackup backup => account.Backup;

        public MyFarms_Partial_EBHistoryModel(MyFarmsModel pageModel, int index, bool isAdmin) : this(
            pageModel.AccountAt(index),
            index,
            pageModel.SnapShots,
            isAdmin,
            pageModel.UncompletedPEContracts,
            pageModel.Contracts,
            pageModel.SeasonPEByEggIncId.GetValueOrDefault(pageModel.AccountAt(index).Backup.EggIncId, (Earned: 0, Max: 0)),
            pageModel.MissingSeasonalPEByEggIncId.GetValueOrDefault(pageModel.AccountAt(index).Backup.EggIncId, new List<MissingSeasonalPe>())
        ) { }
    }
}
