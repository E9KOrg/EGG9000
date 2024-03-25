using System;

namespace EGG9000.Common.Database.Entities {
    public class Merit {
        public Guid Id { get; set; }
        public DateTimeOffset When { get; set; }
        public Guid UserId { get; set; }
        public DBUser User { get; set; }
        public Guid AdminUserId { get; set; }
        public DBUser AdminUser { get; set; }
        public string Reason { get; set; }
    }

}
