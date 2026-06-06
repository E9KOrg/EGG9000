using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace EGG9000.Site.Areas.Identity.Pages.Account.Manage {
    public class PersonalDataModel(
        UserManager<IdentityUser> userManager,
        ILogger<PersonalDataModel> logger) : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly ILogger<PersonalDataModel> _logger = logger;

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            return Page();
        }
    }
}