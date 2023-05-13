using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Database.Entities
{
    public class UserCsHistory {
        public string ContractIdentifier { get; set; }
        public string CoopIdentifier { get; set; }
        public double Cxp { get; set; }

        public UserCsHistory(string contractIdentifier, string coopIdentifier, double cxp) {
            ContractIdentifier = contractIdentifier;
            CoopIdentifier = coopIdentifier;
            Cxp = cxp;
        }

        public override bool Equals(object obj) {
            if(obj is Ei.Contract contract) {
                return contract.Identifier == ContractIdentifier;
            } else if(obj is UserCsHistory history) {
                return history.ContractIdentifier == ContractIdentifier;
            } else return false;
        }
    }
}
