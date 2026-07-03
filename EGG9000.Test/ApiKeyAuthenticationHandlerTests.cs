using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Site.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ApiKeyAuthenticationHandlerTests {
        private static string HashKey(string rawKey)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();

        private static ApiKey MakeKey(string rawKey, bool revoked = false, DateTimeOffset? expiresAt = null)
            => new ApiKey {
                Id = Guid.NewGuid(),
                KeyHash = HashKey(rawKey),
                Label = "test",
                GuildId = 12345UL,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAt = expiresAt,
                Revoked = revoked
            };

        // Minimal stub - returns a default AuthenticationSchemeOptions for any scheme name.
        private class StubOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions> {
            public AuthenticationSchemeOptions CurrentValue => new();
            public AuthenticationSchemeOptions Get(string name) => new();
            public IDisposable OnChange(Action<AuthenticationSchemeOptions, string> listener) => null;
        }

        // Minimal factory shim so we can inject a test DB without touching DI.
        private class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
            : IDbContextFactory<ApplicationDbContext> {
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(options);
        }

        private static async Task<AuthenticateResult> RunHandler(ApiKey storedKey, string headerValue) {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var factory = new TestDbContextFactory(dbOptions);
            if (storedKey != null) {
                using var seed = factory.CreateDbContext();
                seed.ApiKeys.Add(storedKey);
                await seed.SaveChangesAsync();
            }

            var handler = new ApiKeyAuthenticationHandler(
                new StubOptionsMonitor(),
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                factory);

            var context = new DefaultHttpContext();
            if (headerValue != null)
                context.Request.Headers[ApiKeyAuthenticationHandler.HeaderName] = headerValue;

            await handler.InitializeAsync(
                new AuthenticationScheme(ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler)),
                context);

            return await handler.AuthenticateAsync();
        }

        [TestMethod]
        public async Task NoHeader_ReturnsNoResult() {
            var result = await RunHandler(MakeKey("validkey"), headerValue: null);
            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Failure);  // NoResult, not Fail
        }

        [TestMethod]
        public async Task ValidKey_ReturnsSuccess_WithGuildIdClaim() {
            var key = MakeKey("myrawkey");
            var result = await RunHandler(key, "myrawkey");
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("12345", result.Principal!.FindFirst("GuildId")!.Value);
        }

        [TestMethod]
        public async Task WrongKey_ReturnsFail() {
            var key = MakeKey("correctkey");
            var result = await RunHandler(key, "wrongkey");
            Assert.IsFalse(result.Succeeded);
            Assert.IsNotNull(result.Failure);
        }

        [TestMethod]
        public async Task RevokedKey_ReturnsFail() {
            var key = MakeKey("revokedkey", revoked: true);
            var result = await RunHandler(key, "revokedkey");
            Assert.IsFalse(result.Succeeded);
        }

        [TestMethod]
        public async Task ExpiredKey_ReturnsFail() {
            var key = MakeKey("expiredkey", expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
            var result = await RunHandler(key, "expiredkey");
            Assert.IsFalse(result.Succeeded);
        }

        [TestMethod]
        public async Task NotYetExpiredKey_ReturnsSuccess() {
            var key = MakeKey("futurekey", expiresAt: DateTimeOffset.UtcNow.AddDays(30));
            var result = await RunHandler(key, "futurekey");
            Assert.IsTrue(result.Succeeded);
        }

        private static async Task<(AuthenticateResult Result, ApplicationDbContext Db)> RunHandlerWithDb(ApiKey storedKey, string headerValue, string remoteIp = "203.0.113.5", string dbName = null) {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
                .Options;
            var factory = new TestDbContextFactory(dbOptions);
            if (storedKey != null) {
                using var seed = factory.CreateDbContext();
                seed.ApiKeys.Add(storedKey);
                await seed.SaveChangesAsync();
            }

            var handler = new ApiKeyAuthenticationHandler(
                new StubOptionsMonitor(),
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                factory);

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
            if (headerValue != null)
                context.Request.Headers[ApiKeyAuthenticationHandler.HeaderName] = headerValue;

            await handler.InitializeAsync(
                new AuthenticationScheme(ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler)),
                context);

            var result = await handler.AuthenticateAsync();
            return (result, factory.CreateDbContext());
        }

        [TestMethod]
        public async Task ValidKey_WritesRequestLogAndDailyUsage() {
            var key = MakeKey("logtestkey");
            var (result, db) = await RunHandlerWithDb(key, "logtestkey");

            Assert.IsTrue(result.Succeeded);

            var logRow = await db.ApiKeyRequestLogs.SingleAsync();
            Assert.AreEqual(key.Id, logRow.ApiKeyId);
            Assert.AreEqual(12345UL, logRow.GuildId);
            Assert.AreEqual("203.0.113.5", logRow.IpAddress);
            Assert.IsTrue(logRow.Success);

            var usageRow = await db.ApiKeyDailyUsages.SingleAsync();
            Assert.AreEqual(key.Id, usageRow.ApiKeyId);
            Assert.AreEqual(1, usageRow.RequestCount);
        }

        [TestMethod]
        public async Task ValidKey_SecondRequestSameDay_IncrementsDailyUsage() {
            var key = MakeKey("counterkey");
            var dbName = Guid.NewGuid().ToString();
            await RunHandlerWithDb(key, "counterkey", dbName: dbName);
            var (_, db) = await RunHandlerWithDb(storedKey: null, headerValue: "counterkey", dbName: dbName);

            var usageRow = await db.ApiKeyDailyUsages.SingleAsync();
            Assert.AreEqual(2, usageRow.RequestCount);
        }

        [TestMethod]
        public async Task UnmatchedKey_WritesRequestLogWithNullApiKeyId() {
            var (result, db) = await RunHandlerWithDb(storedKey: null, headerValue: "nonexistentkey");

            Assert.IsFalse(result.Succeeded);

            var logRow = await db.ApiKeyRequestLogs.SingleAsync();
            Assert.IsNull(logRow.ApiKeyId);
            Assert.IsNull(logRow.GuildId);
            Assert.IsFalse(logRow.Success);
            Assert.AreEqual("203.0.113.5", logRow.IpAddress);
        }

        [TestMethod]
        public async Task NoHeader_DoesNotWriteRequestLog() {
            var (_, db) = await RunHandlerWithDb(MakeKey("unused"), headerValue: null);

            Assert.AreEqual(0, await db.ApiKeyRequestLogs.CountAsync());
        }
    }
}
