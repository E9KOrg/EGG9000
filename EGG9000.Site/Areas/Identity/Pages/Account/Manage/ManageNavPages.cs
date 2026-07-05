using Microsoft.AspNetCore.Mvc.Rendering;
using System;

namespace EGG9000.Site.Areas.Identity.Pages.Account.Manage {
    public static class ManageNavPages {
        public static string Index {
            get {
                return "Index";
            }
        }

        public static string Email {
            get {
                return "Email";
            }
        }

        public static string ChangePassword {
            get {
                return "ChangePassword";
            }
        }

        public static string ExternalLogins {
            get {
                return "ExternalLogins";
            }
        }

        public static string PersonalData {
            get {
                return "PersonalData";
            }
        }

        public static string TwoFactorAuthentication {
            get {
                return "TwoFactorAuthentication";
            }
        }

        public static string IndexNavClass(ViewContext viewContext) {
            return PageNavClass(viewContext, Index);
        }

        public static string EmailNavClass(ViewContext viewContext) {
            return PageNavClass(viewContext, Email);
        }

        public static string ChangePasswordNavClass(ViewContext viewContext) {
            return PageNavClass(viewContext, ChangePassword);
        }

        public static string ExternalLoginsNavClass(ViewContext viewContext) {
            return PageNavClass(viewContext, ExternalLogins);
        }

        public static string PersonalDataNavClass(ViewContext viewContext) {
            return PageNavClass(viewContext, PersonalData);
        }

        public static string TwoFactorAuthenticationNavClass(ViewContext viewContext) {
            return PageNavClass(viewContext, TwoFactorAuthentication);
        }

        private static string PageNavClass(ViewContext viewContext, string page) {
            var activePage = viewContext.ViewData["ActivePage"] as string
                ?? System.IO.Path.GetFileNameWithoutExtension(viewContext.ActionDescriptor.DisplayName);
            return string.Equals(activePage, page, StringComparison.OrdinalIgnoreCase) ? "active" : null;
        }
    }
}
