using EGG9000.Common.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Site.Areas.Identity.Pages.Account.Manage {
    public partial class IndexModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ApplicationDbContext db
            ) : PageModel {
        private readonly UserManager<IdentityUser> _userManager = userManager;
        private readonly SignInManager<IdentityUser> _signInManager = signInManager;
        private readonly ApplicationDbContext _db = db;

        [BindProperty]
        public string Username { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel {
            //[Phone]
            //[Display(Name = "Phone number")]
            //public string PhoneNumber { get; set; }
            [Display(Name = "Get a DM when a ship returns")]
            public bool DMOnShipReturn { get; set; }
            [Display(Name = "How Many Minutes before a ship returns that you'll get a DM")]
            public int ShipReturnMinutes { get; set; }
            [Display(Name = "How Many Minutes before a ship returns that you'll get a DM if fuel isn't full")]
            public int ShipReturnStillFuelingMinutes { get; set; }
            [Display(Name = "Append your EB to your Discord Username")]
            public bool ShowEB { get; set; }
            [Display(Name = "Take A Break (Stops pings for new contracts until you start prefarming again, you still have to do any contracts you are prefarming)")]
            public bool OnBreak { get; set; }
            [Display(Name = "Egg of Prophecy")]
            public bool SkipNoPE { get; set; }
            [Display(Name = "Artifacts")]
            public bool SkipNoArtifacts { get; set; }
            [Display(Name = "Piggy doubler")]
            public bool SkipNoPiggyDouble { get; set; }
        }

        private async Task LoadAsync(IdentityUser user) {
            var userName = await _userManager.GetUserNameAsync(user);
            //var phoneNumber = await _userManager.GetPhoneNumberAsync(user);

            var logins = await _userManager.GetLoginsAsync(user);
            var dbuser = await _db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
            Username = userName;


            Input = new InputModel {
                //PhoneNumber = phoneNumber,
                DMOnShipReturn = dbuser.DMOnShipReturn,
                ShipReturnMinutes = dbuser.ShipReturnMinutes,
                ShipReturnStillFuelingMinutes = dbuser.ShipReturnStillFuelingMinutes,
                ShowEB = dbuser.showEB,
                SkipNoPE = dbuser.SkipNoPE,
                SkipNoPiggyDouble = dbuser.SkipNoPiggyDouble,
                SkipNoArtifacts = dbuser.SkipNoArtifacts,
                OnBreak = dbuser.OnBreakSince.HasValue
            };
        }

        public async Task<IActionResult> OnGetAsync() {
            var user = await _userManager.GetUserAsync(User);
            if(user == null) {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync() {
            var user = await _userManager.GetUserAsync(User);
            if(user == null) {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            if(!ModelState.IsValid) {
                await LoadAsync(user);
                return Page();
            }

            //var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            //if(Input.PhoneNumber != phoneNumber) {
            //    var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
            //    if(!setPhoneResult.Succeeded) {
            //        var userId = await _userManager.GetUserIdAsync(user);
            //        throw new InvalidOperationException($"Unexpected error occurred setting phone number for user with ID '{userId}'.");
            //    }
            //}

            if(!string.IsNullOrEmpty(Username)) {
                Console.WriteLine("Updating username");
                user.UserName = Username;
                await _userManager.UpdateAsync(user);
            }
            var logins = await _userManager.GetLoginsAsync(user);
            var dbuser = await _db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
            dbuser.DMOnShipReturn = Input.DMOnShipReturn;
            dbuser.ShipReturnMinutes = Input.ShipReturnMinutes;
            dbuser.ShipReturnStillFuelingMinutes = Input.ShipReturnStillFuelingMinutes;
            dbuser.SkipNoPE = Input.SkipNoPE;
            dbuser.SkipNoArtifacts = Input.SkipNoArtifacts;
            dbuser.SkipNoPiggyDouble = Input.SkipNoPiggyDouble;
            dbuser.showEB = Input.ShowEB;
            if(Input.OnBreak && !dbuser.OnBreakSince.HasValue) {
                dbuser.OnBreakSince = DateTimeOffset.UtcNow;
            }
            await _db.SaveChangesAsync();

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Your profile has been updated";
            return RedirectToPage();
        }
    }
}
