using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EGG9000.Site.Areas.Identity.Pages.Account.Manage {
    public partial class IndexModel : PageModel {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ApplicationDbContext _db;

        public IndexModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            ApplicationDbContext db
            ) {
            _userManager = userManager;
            _signInManager = signInManager;
            _db = db;
        }


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
                ShipReturnStillFuelingMinutes = dbuser.ShipReturnStillFuelingMinutes
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


            var logins = await _userManager.GetLoginsAsync(user);
            var dbuser = await _db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == ulong.Parse(logins.First().ProviderKey));
            dbuser.DMOnShipReturn = Input.DMOnShipReturn;
            dbuser.ShipReturnMinutes = Input.ShipReturnMinutes;
            dbuser.ShipReturnStillFuelingMinutes = Input.ShipReturnStillFuelingMinutes;
            await _db.SaveChangesAsync();

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Your profile has been updated";
            return RedirectToPage();
        }
    }
}
