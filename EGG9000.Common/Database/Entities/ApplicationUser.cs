using Microsoft.AspNetCore.Identity;

namespace EGG9000.Common.Database.Entities {
    // ASP.NET Identity user. Carries web-only preferences (e.g. dark mode) that do not belong on
    // the bot-domain DBUser. Registered via AddIdentity<ApplicationUser, IdentityRole> in the Site.
    public class ApplicationUser : IdentityUser {
        public bool DarkMode { get; set; }
    }
}
