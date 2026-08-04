using System.Linq;
using System.Security.Claims;

namespace EGG9000.Site.Auth {
    public static class ClaimsPrincipalExtensions {
        public static string DiscordId(this ClaimsPrincipal user) =>
            user.Claims.First(x => x.Type == "DiscordId").Value;

        public static bool IsStaff(this ClaimsPrincipal user) =>
            user.IsInRole("Admin") || user.IsInRole("GuildAdmin") || user.IsInRole("GuildLesserAdmin") || user.IsInRole("GuildReadOnlyAdmin");
    }
}
