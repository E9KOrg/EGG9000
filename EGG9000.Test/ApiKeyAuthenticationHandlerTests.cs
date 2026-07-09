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
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ApiKeyAuthenticationHandlerTests {
        private static string HashKey(string rawKey) {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))).ToLowerInvariant();
        }

        private static ApiKey MakeKey(string rawKey, bool revoked = false, DateTimeOffset? expiresAt = null) {
            return new() {
                Id = Guid.NewGuid(),
                KeyHash = HashKey(rawKey),
                Label = "test",
                GuildId = 12345UL,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAt = expiresAt,
                Revoked = revoked
            };
        }

        // Minimal stub - returns a default AuthenticationSchemeOptions for any scheme name.
        private class StubOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions> {
            public AuthenticationSchemeOptions CurrentValue {
                get {
                    return new();
                }
            }

            public AuthenticationSchemeOptions Get(string? name) {
                return new();
            }

            public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string> listener) {
                return null;
            }
        }

        // Minimal factory shim so we can inject a test DB without touching DI.
        private class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
            : IDbContextFactory<ApplicationDbContext> {
            public ApplicationDbContext CreateDbContext() {
                return new(options);
            }
        }

        private static async Task<AuthenticateResult> RunHandler(ApiKey storedKey, string? headerValue) {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var factory = new TestDbContextFactory(dbOptions);
            if(storedKey != null) {
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
            if(headerValue != null)
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

        private static async Task<(AuthenticateResult Result, ApplicationDbContext Db)> RunHandlerWithDb(ApiKey? storedKey, string? headerValue, string remoteIp = "203.0.113.5", string? dbName = null) {
            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
                .Options;
            var factory = new TestDbContextFactory(dbOptions);
            if(storedKey != null) {
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
            context.Request.Method = "GET";
            context.Request.Path = "/LeaderboardJson";
            if(headerValue != null)
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

            var logRow = await db.ApiKeyRequestLogs.SingleAsync(TestContext!.CancellationToken);
            Assert.AreEqual(key.Id, logRow.ApiKeyId);
            Assert.AreEqual(12345UL, logRow.GuildId);
            Assert.AreEqual("203.0.113.5", logRow.IpAddress);
            Assert.AreEqual("GET /LeaderboardJson", logRow.Endpoint);
            Assert.IsTrue(logRow.Success);

            var usageRow = await db.ApiKeyDailyUsages.SingleAsync(TestContext.CancellationToken);
            Assert.AreEqual(key.Id, usageRow.ApiKeyId);
            Assert.AreEqual(1, usageRow.RequestCount);
        }

        [TestMethod]
        public async Task ValidKey_SecondRequestSameDay_IncrementsDailyUsage() {
            var key = MakeKey("counterkey");
            var dbName = Guid.NewGuid().ToString();
            await RunHandlerWithDb(key, "counterkey", dbName: dbName);
            var (_, db) = await RunHandlerWithDb(storedKey: null, headerValue: "counterkey", dbName: dbName);

            var usageRow = await db.ApiKeyDailyUsages.SingleAsync(TestContext!.CancellationToken);
            Assert.AreEqual(2, usageRow.RequestCount);
        }

        [TestMethod]
        public async Task UnmatchedKey_WritesRequestLogWithNullApiKeyId() {
            var (result, db) = await RunHandlerWithDb(storedKey: null, headerValue: "nonexistentkey");

            Assert.IsFalse(result.Succeeded);

            var logRow = await db.ApiKeyRequestLogs.SingleAsync(TestContext!.CancellationToken);
            Assert.IsNull(logRow.ApiKeyId);
            Assert.IsNull(logRow.GuildId);
            Assert.IsFalse(logRow.Success);
            Assert.AreEqual("203.0.113.5", logRow.IpAddress);
        }

        [TestMethod]
        public async Task NoHeader_DoesNotWriteRequestLog() {
            var (_, db) = await RunHandlerWithDb(MakeKey("unused"), headerValue: null);

            Assert.AreEqual(0, await db.ApiKeyRequestLogs.CountAsync(TestContext!.CancellationToken));
        }

        [TestMethod]
        public async Task ValidKey_StillSucceeds_WhenLogWriteThrows() {
            var key = MakeKey("resilientkey");
            var dbName = Guid.NewGuid().ToString();

            var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            var factory = new TestDbContextFactory(dbOptions);
            using(var seed = factory.CreateDbContext()) {
                seed.ApiKeys.Add(key);
                await seed.SaveChangesAsync(TestContext!.CancellationToken);
            }

            var brokenFactory = new ThrowingDbContextFactory(dbOptions);
            var handler = new ApiKeyAuthenticationHandler(
                new StubOptionsMonitor(),
                NullLoggerFactory.Instance,
                UrlEncoder.Default,
                brokenFactory);

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5");
            context.Request.Headers[ApiKeyAuthenticationHandler.HeaderName] = "resilientkey";

            await handler.InitializeAsync(
                new AuthenticationScheme(ApiKeyAuthenticationHandler.SchemeName, null, typeof(ApiKeyAuthenticationHandler)),
                context);

            var result = await handler.AuthenticateAsync();

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("12345", result.Principal!.FindFirst("GuildId")!.Value);
        }

        // Wraps a real ApplicationDbContext but makes SaveChangesAsync throw on every call after the
        // first, so the key lookup succeeds but the subsequent log write fails - proving auth doesn't
        // depend on the log write succeeding.
        private class ThrowingDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext> {
            public ApplicationDbContext CreateDbContext() {
                return new ThrowingSaveChangesDbContext(options);
            }
        }

        private class ThrowingSaveChangesDbContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options) {
            public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) {
                throw new InvalidOperationException("Simulated DB failure during log write.");
            }
        }

        public TestContext? TestContext { get; set; }
    }
}
