using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Common.Database.Entities {
    public class Demerit {
        public Guid Id { get; set; }
        public DateTimeOffset When { get; set; }
        public Guid UserId { get; set; }
        public DBUser User { get; set; }
        public Guid AdminUserId { get; set; }
        public DBUser AdminUser { get; set; }
        public string Reason { get; set; }
        public bool Permanent { get; set; }
        public string ContractID { get; set; }
    }

}
