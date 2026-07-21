using EGG9000.Common.Database;
using System.Collections.Generic;

namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_TestAssignmentModel(
        EggIncAccount account,
        int index,
        List<DBContract> contracts
    ) : MyFarms_Partial_BaseModel(account, index) {
        public MyFarms_Partial_TestAssignmentModel(MyFarmsModel pageModel, int index) : this(
            pageModel.AccountAt(index),
            index,
            pageModel.Contracts
        ) { }
    }
}
