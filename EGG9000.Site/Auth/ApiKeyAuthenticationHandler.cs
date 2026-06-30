using EGG9000.Common.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace EGG9000.Site.Auth {
    public class ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDbContextFactory<ApplicationDbContext> dbFactory)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder) {

        public const string SchemeName = "ApiKey";
        public const string HeaderName = "X-Api-Key";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (!Request.Headers.TryGetValue(HeaderName, out var rawKeyValues) || rawKeyValues.Count == 0)
                return AuthenticateResult.NoResult();

            var rawKey = rawKeyValues[0]!.Trim();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

            await using var db = dbFactory.CreateDbContext();
            var key = await db.ApiKeys.FirstOrDefaultAsync(k =>
                k.KeyHash == hash &&
                !k.Revoked &&
                (k.ExpiresAt == null || k.ExpiresAt > DateTimeOffset.UtcNow));

            if (key == null)
                return AuthenticateResult.Fail("Invalid or expired API key.");

            var claims = new[] {
                new Claim("GuildId", key.GuildId.ToString()),
                new Claim("ApiKeyId", key.Id.ToString())
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return AuthenticateResult.Success(ticket);
        }
    }
}
