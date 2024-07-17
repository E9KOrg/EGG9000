using EGG9000.Common.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EGG9000.Site.Data {
    public class CustomClaimsPrincipleFactory(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, IOptions<IdentityOptions> optionsAccessor, ApplicationDbContext db) 
        : UserClaimsPrincipalFactory<IdentityUser, IdentityRole>(userManager, roleManager, optionsAccessor) {

        private readonly ApplicationDbContext _db = db;
        private readonly UserManager<IdentityUser> _userManager = userManager;

        protected async override Task<ClaimsIdentity> GenerateClaimsAsync(IdentityUser user) {
            var identity = await base.GenerateClaimsAsync(user);
            var logins = await _userManager.GetLoginsAsync(user);
            var dbuser = await _db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey)) 
                ?? throw new DiscordAccountNotFoundException("This discord account isn't registered with the bot EGG9000.");
            identity.AddClaim(new Claim("DbUserId", dbuser.Id.ToString()));
            identity.AddClaim(new Claim("DiscordId", logins.First().ProviderKey));
            identity.AddClaim(new Claim("GuildId", dbuser.GuildId.ToString()));
            return identity;
        }

        public class DiscordAccountNotFoundException : Exception {
            public DiscordAccountNotFoundException() {
            }

            public DiscordAccountNotFoundException(string message)
                : base(message) {
            }

            public DiscordAccountNotFoundException(string message, Exception inner)
                : base(message, inner) {
            }
        }
    }
}
