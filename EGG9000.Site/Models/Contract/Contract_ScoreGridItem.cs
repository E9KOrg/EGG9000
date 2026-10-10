namespace EGG9000.Site.Models.Contract {
    public record Contract_ScoreGridItem {
        public string ContractId { get; set; }
        public double Score { get; set; }
        public string Name { get; set; }
        public string RoleId { get; set; }
    }

    public record Contract_ScoreGridContract {
        public string ContractId { get; set; }
        public string ContractName { get; set; }
    }
}
