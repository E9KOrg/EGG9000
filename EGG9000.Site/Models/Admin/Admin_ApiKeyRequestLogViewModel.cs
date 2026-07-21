using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_ApiKeyRequestLogViewModel {
        public string KeyLabel { get; set; }
        public List<ApiKeyRequestLog> Entries { get; set; }
    }
}
