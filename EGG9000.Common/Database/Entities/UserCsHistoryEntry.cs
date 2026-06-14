using Microsoft.EntityFrameworkCore;

using System;

namespace EGG9000.Common.Database.Entities {
    [Index(nameof(ContractIdentifier))]
    public class UserCsHistoryEntry(string contractIdentifier, string coopIdentifier, double cxp, string eggIncId) {
        public string ContractIdentifier { get; set; } = contractIdentifier;
        public string CoopIdentifier { get; set; } = coopIdentifier;
        public string EggIncId { get; set; } = eggIncId;
        public double Cxp { get; set; } = cxp;
        public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

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
