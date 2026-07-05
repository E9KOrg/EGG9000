using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
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

        protected async override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if(!Request.Headers.TryGetValue(HeaderName, out var rawKeyValues) || rawKeyValues.Count == 0)
                return AuthenticateResult.NoResult();

            var rawKey = rawKeyValues[0]!.Trim();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
            var ipAddress = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var endpoint = $"{Request.Method} {Request.Path}";

            await using var db = dbFactory.CreateDbContext();
            var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash);

            var valid = key != null && !key.Revoked && (key.ExpiresAt == null || key.ExpiresAt > DateTimeOffset.UtcNow);

            try {
                await LogRequestAsync(db, key, ipAddress, endpoint, valid);
            } catch(Exception ex) {
                Logger.LogError(ex, "Failed to write API key request log/usage for key {ApiKeyId}.", key?.Id);
            }

            if(!valid)
                return AuthenticateResult.Fail("Invalid or expired API key.");

            var claims = new[] {
                new Claim("GuildId", key.GuildId.ToString()),
                new Claim("ApiKeyId", key.Id.ToString())
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return AuthenticateResult.Success(ticket);
        }

        private static async Task LogRequestAsync(ApplicationDbContext db, ApiKey key, string ipAddress, string endpoint, bool success) {
            var now = DateTimeOffset.UtcNow;

            db.ApiKeyRequestLogs.Add(new ApiKeyRequestLog {
                Id = Guid.NewGuid(),
                ApiKeyId = key?.Id,
                GuildId = key?.GuildId,
                IpAddress = ipAddress,
                Endpoint = endpoint,
                Timestamp = now,
                Success = success
            });

            await db.SaveChangesAsync();

            if(key != null)
                await IncrementDailyUsageAsync(db, key.Id, now.UtcDateTime.Date);
        }

        private static async Task IncrementDailyUsageAsync(ApplicationDbContext db, Guid apiKeyId, DateTime today) {
            if(db.Database.IsNpgsql()) {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "ApiKeyDailyUsages" ("ApiKeyId", "Date", "RequestCount")
                    VALUES ({apiKeyId}, {today}, 1)
                    ON CONFLICT ("ApiKeyId", "Date") DO UPDATE SET "RequestCount" = "ApiKeyDailyUsages"."RequestCount" + 1
                    """);
                return;
            }

            var usage = await db.ApiKeyDailyUsages.FirstOrDefaultAsync(u => u.ApiKeyId == apiKeyId && u.Date == today);
            if(usage == null) {
                db.ApiKeyDailyUsages.Add(new ApiKeyDailyUsage { ApiKeyId = apiKeyId, Date = today, RequestCount = 1 });
            } else {
                usage.RequestCount++;
            }
            await db.SaveChangesAsync();
        }
    }
}
