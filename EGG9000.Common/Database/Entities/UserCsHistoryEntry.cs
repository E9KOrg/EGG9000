using Microsoft.EntityFrameworkCore;

using System;

namespace EGG9000.Common.Database.Entities {
    [Index(nameof(ContractIdentifier))]
    public class UserCsHistoryEntry {
        public string ContractIdentifier { get; set; }
        public string CoopIdentifier { get; set; }
        public string EggIncId { get; set; }
        public double Cxp { get; set; }
        public DateTimeOffset Created { get; set; }

        public UserCsHistoryEntry(string contractIdentifier, string coopIdentifier, double cxp, string eggIncId) {
            ContractIdentifier = contractIdentifier;
            CoopIdentifier = coopIdentifier;
            Cxp = cxp;
            EggIncId = eggIncId;
            Created = DateTimeOffset.UtcNow; 
        }

        public override bool Equals(object obj) {
            if(obj is Ei.Contract contract) {
                return contract.Identifier == ContractIdentifier;
            } else if(obj is UserCsHistoryEntry history) {
                return history.ContractIdentifier == ContractIdentifier;
            } else return false;
        }

        public override int GetHashCode() {
           return ContractIdentifier.GetHashCode() ^ CoopIdentifier.GetHashCode();
        }
    }
}
