using System;

namespace EGG9000.Common.Database.Entities {
    public class Donation {
        public Guid Id { get; set; }
        public DateTimeOffset When { get; set; }
        public Guid UserId { get; set; }
        public DBUser User { get; set; }
        public float Amount { get; set; }
        public string Type { get; set; }
    }

}
