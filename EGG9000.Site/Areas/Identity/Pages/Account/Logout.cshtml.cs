using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EGG9000.Site.Areas.Identity.Pages.Account {
    [AllowAnonymous]
    public class LogoutModel(SignInManager<ApplicationUser> signInManager, ILogger<LogoutModel> logger) : PageModel {
        private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
        private readonly ILogger<LogoutModel> _logger = logger;

        public async Task<IActionResult> OnGet() {
            await SignOutEverywhereAsync();
            return Page();
        }

        public async Task<IActionResult> OnPost(string returnUrl = null) {
            await SignOutEverywhereAsync();
            if(returnUrl != null) {
                return LocalRedirect(returnUrl);
            } else {
                return RedirectToPage();
            }
        }

        private async Task SignOutEverywhereAsync() {
            await _signInManager.SignOutAsync();
            await HttpContext.SignOutAsync(IdentityConstants.TwoFactorRememberMeScheme);
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
            Response.Headers.CacheControl = "no-store, no-cache, max-age=0, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            _logger.LogInformation("User logged out.");
        }
    }
}
