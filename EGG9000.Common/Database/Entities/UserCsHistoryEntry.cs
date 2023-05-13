using Microsoft.EntityFrameworkCore.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Database.Entities
{
    [MessagePack.MessagePackObject]
    public class UserCsHistoryEntry {
        [MessagePack.Key(0)]
        public string ContractIdentifier { get; set; }
        [MessagePack.Key(1)]
        public string CoopIdentifier { get; set; }
        [MessagePack.Key(2)]
        public double Cxp { get; set; }

        public UserCsHistoryEntry(string contractIdentifier, string coopIdentifier, double cxp) {
            ContractIdentifier = contractIdentifier;
            CoopIdentifier = coopIdentifier;
            Cxp = cxp;
        }

        public override bool Equals(object obj) {
            if(obj is Ei.Contract contract) {
                return contract.Identifier == ContractIdentifier;
            } else if(obj is UserCsHistoryEntry history) {
                return history.ContractIdentifier == ContractIdentifier;
            } else return false;
        }
    }
}
