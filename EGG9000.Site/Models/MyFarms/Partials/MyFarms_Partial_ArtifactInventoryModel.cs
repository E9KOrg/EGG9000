namespace EGG9000.Site.Models.MyFarms {
    public record MyFarms_Partial_ArtifactInventoryModel(
        EggIncAccount account,
        int index
    ) : MyFarms_Partial_BaseModel(account, index) {
        public string AccountId => account.Id;

        public MyFarms_Partial_ArtifactInventoryModel(MyFarmsModel pageModel, int index) : this(
            pageModel.AccountAt(index),
            index
        ) { }
    }
}
