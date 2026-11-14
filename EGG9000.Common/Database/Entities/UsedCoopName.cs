using Microsoft.EntityFrameworkCore;
using System;

namespace EGG9000.Common.Database.Entities {
    [PrimaryKey(nameof(ContractID), nameof(Name))]
    [Index(nameof(Name))]
    public class UsedCoopName {
        public const string AnyContract = "";

        public string ContractID { get; set; }
        public string Name { get; set; }
        public DateTimeOffset FirstUsed { get; set; }
    }
}
