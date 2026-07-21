using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Site.Controllers {
    public abstract class E9KControllerBase : Controller {
        protected ulong GetGuildId() {
            return ulong.Parse(((ClaimsIdentity)User.Identity).Claims.First(x => x.Type == "GuildId").Value);
        }

        protected async Task<DBUser> GetCurrentDbUserAsync() {
            var db = HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var userManager = HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var loginUser = await userManager.GetUserAsync(User);
            var logins = await userManager.GetLoginsAsync(loginUser);
            return await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
        }

        protected IActionResult RedirectToLocalReferer() {
            var referer = Request.Headers["Referer"].ToString();
            if(Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == Request.Host.Host) {
                return Redirect(uri.PathAndQuery);
            }
            return Redirect("~/");
        }
    }
}
