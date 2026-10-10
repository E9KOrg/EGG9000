using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_UserPermissionsModel {
        public List<ApplicationUser> Users { get; set; }
        public List<IdentityUserLogin<string>> Logins { get; set; }
        public List<IdentityUserRole<string>> UserRoles { get; set; }
        public List<IdentityRole> Roles { get; set; }
        public List<DBUser> DbUsers { get; set; }
    }
}
