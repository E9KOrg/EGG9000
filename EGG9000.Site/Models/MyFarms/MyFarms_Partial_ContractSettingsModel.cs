namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_ContractSettingsModel(
        EggIncAccount account,
        int index,
        bool isSelf
    ) : MyFarms_Partial_BaseModel(account, index) {
        public MyFarms_Partial_ContractSettingsModel(MyFarmsModel pageModel, int index, bool isSelf) : this(
            pageModel.AccountAt(index),
            index,
            isSelf
        ) { }
    }
}