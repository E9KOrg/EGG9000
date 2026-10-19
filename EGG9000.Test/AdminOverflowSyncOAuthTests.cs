using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Site.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class AdminOverflowSyncOAuthTests {
        private const ulong GuildId = 100;
        private const string AccessToken = "user-oauth-token-123";
        private const string ReturnUrl = "https://site.test/Admin/DiscordReturn";

        private sealed class MemoryTempDataProvider : ITempDataProvider {
            private Dictionary<string, object> _values = [];

            public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(_values);

            public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = new Dictionary<string, object>(values);

            public IEnumerable<object> Stored => _values.Values;
        }

        private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler {
            public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
                Requests.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
                return new HttpResponseMessage(status) { Content = new StringContent(body) };
            }
        }

        private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory {
            public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
        }

        private sealed class FixedUrlHelper(ActionContext context) : IUrlHelper {
            public ActionContext ActionContext => context;

            public string Action(UrlActionContext actionContext) => ReturnUrl;

            public string Content(string? contentPath) => contentPath ?? string.Empty;

            public bool IsLocalUrl(string? url) => true;

            public string Link(string? routeName, object? values) => ReturnUrl;

            public string RouteUrl(UrlRouteContext routeContext) => ReturnUrl;
        }

        private sealed record Harness(AdminController Controller, RecordingHandler Handler, MemoryTempDataProvider TempData);

        private static Guild DbGuild(params ulong[] overflows) => new() {
            Id = GuildId,
            DiscordSeverId = GuildId,
            Name = "main",
            OverflowServersJson = $"[{string.Join(",", overflows)}]"
        };

        private static async Task<ServiceProvider> Services(Guild guild) {
            var name = Guid.NewGuid().ToString();
            var provider = new ServiceCollection()
                .AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name))
                .BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Guilds.Add(guild);
            await db.SaveChangesAsync();
            return provider;
        }

        private static Harness Build(IServiceProvider services, MemoryTempDataProvider? tempData = null, HttpStatusCode tokenStatus = HttpStatusCode.OK, string? tokenBody = null) {
            var handler = new RecordingHandler(tokenStatus, tokenBody ?? $$"""{"access_token":"{{AccessToken}}","token_type":"Bearer"}""");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> {
                    ["ConnectionStrings:ClientId"] = "client-id",
                    ["ConnectionStrings:ClientSecret"] = "client-secret"
                })
                .Build();
            var discord = new FakeDiscord();
            var controller = new AdminController(null!, discord.Gateway, null!, null!, NullLogger<AdminController>.Instance, configuration, null!, new HandlerFactory(handler), null!);

            var httpContext = new DefaultHttpContext {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("GuildId", GuildId.ToString())], "test"))
            };
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.Url = new FixedUrlHelper(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()));
            tempData ??= new MemoryTempDataProvider();
            controller.TempData = new TempDataDictionary(httpContext, tempData);
            return new Harness(controller, handler, tempData);
        }

        private static Dictionary<string, string> Query(string url) =>
            new Uri(url).Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));

        private static async Task<string> StartSync(Harness harness) {
            var redirect = (RedirectResult)await harness.Controller.Sync();
            harness.Controller.TempData.Save();
            return Query(redirect.Url)["state"];
        }

        [TestMethod]
        public async Task Sync_RedirectsToDiscordWithFreshStateAndNarrowScope() {
            await using var services = await Services(DbGuild(200));
            var harness = Build(services);

            var redirect = (RedirectResult)await harness.Controller.Sync();
            var uri = new Uri(redirect.Url);
            var query = Query(redirect.Url);

            Assert.AreEqual("discord.com", uri.Host);
            Assert.AreEqual("/api/oauth2/authorize", uri.AbsolutePath);
            Assert.AreEqual("code", query["response_type"]);
            Assert.AreEqual("client-id", query["client_id"]);
            Assert.AreEqual("identify applications.commands.permissions.update", query["scope"]);
            Assert.AreEqual(ReturnUrl, query["redirect_uri"]);
            Assert.AreEqual(32, query["state"].Length);
            Assert.AreEqual(query["state"], harness.Controller.TempData["OverflowSyncState"]);
            Assert.AreNotEqual("15773059ghq9183habn", query["state"]);
        }

        [TestMethod]
        public async Task Sync_StateDiffersPerRequest() {
            await using var services = await Services(DbGuild(200));

            var first = await StartSync(Build(services));
            var second = await StartSync(Build(services));

            Assert.AreNotEqual(first, second);
        }

        [TestMethod]
        public async Task Sync_GuildWithoutOverflowsNeverStartsOAuth() {
            await using var services = await Services(DbGuild());
            var harness = Build(services);

            var result = await harness.Controller.Sync();

            Assert.AreEqual("This server has no overflow servers to sync.", ((ContentResult)result).Content);
            Assert.IsFalse(harness.Controller.TempData.ContainsKey("OverflowSyncState"));
        }

        [TestMethod]
        [DataRow(null, "state")]
        [DataRow("", "state")]
        [DataRow("code", null)]
        [DataRow("code", "")]
        [DataRow("code", "wrong")]
        [DataRow("code", "0000000000000000000000000000000")]
        public async Task DiscordReturn_BadOrMissingCodeOrStateIsRejectedBeforeTokenExchange(string? code, string? stateOverride) {
            await using var services = await Services(DbGuild(200));
            var tempData = new MemoryTempDataProvider();
            var expected = await StartSync(Build(services, tempData));
            var harness = Build(services, tempData);

            var state = stateOverride == "state" ? expected : stateOverride;
            var result = await harness.Controller.DiscordReturn(code!, state!);

            Assert.AreEqual("OAuth state did not match. Start the sync again from the admin page.", ((BadRequestObjectResult)result).Value);
            Assert.AreEqual(0, harness.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task DiscordReturn_WithoutAPriorSyncIsRejected() {
            await using var services = await Services(DbGuild(200));
            var harness = Build(services);

            var result = await harness.Controller.DiscordReturn("code", "AABBCCDDEEFF00112233445566778899");

            Assert.IsInstanceOfType<BadRequestObjectResult>(result);
            Assert.AreEqual(0, harness.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task DiscordReturn_MatchingStateExchangesCodeAndNeverExposesOrStoresTheToken() {
            await using var services = await Services(DbGuild(200));
            var tempData = new MemoryTempDataProvider();
            var state = await StartSync(Build(services, tempData));
            var harness = Build(services, tempData);

            var result = await harness.Controller.DiscordReturn("auth-code", state);
            harness.Controller.TempData.Save();

            var content = (ContentResult)result;
            Assert.AreEqual("The bot is not in this server.", content.Content);
            var (request, body) = harness.Handler.Requests.Single();
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://discord.com/api/oauth2/token", request.RequestUri!.ToString());
            StringAssert.Contains(body, "grant_type=authorization_code");
            StringAssert.Contains(body, "code=auth-code");
            StringAssert.Contains(body, "client_secret=client-secret");
            StringAssert.Contains(body, $"redirect_uri={Uri.EscapeDataString(ReturnUrl)}");
            Assert.IsFalse(tempData.Stored.Any(x => x?.ToString()?.Contains(AccessToken) == true));
            Assert.IsFalse(harness.Controller.TempData.Values.Any(x => x?.ToString()?.Contains(AccessToken) == true));
            Assert.IsFalse(content.Content!.Contains(AccessToken));
        }

        [TestMethod]
        public async Task DiscordReturn_StateIsSingleUse() {
            await using var services = await Services(DbGuild(200));
            var tempData = new MemoryTempDataProvider();
            var state = await StartSync(Build(services, tempData));

            var first = Build(services, tempData, HttpStatusCode.BadRequest, "{}");
            var firstResult = await first.Controller.DiscordReturn("code", state);
            first.Controller.TempData.Save();
            var replay = Build(services, tempData);
            var replayResult = await replay.Controller.DiscordReturn("code", state);

            Assert.AreEqual("Failed to retrieve access token.", ((BadRequestObjectResult)firstResult).Value);
            Assert.AreEqual("OAuth state did not match. Start the sync again from the admin page.", ((BadRequestObjectResult)replayResult).Value);
            Assert.AreEqual(0, replay.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task DiscordReturn_TokenResponseWithoutAccessTokenIsRejected() {
            await using var services = await Services(DbGuild(200));
            var tempData = new MemoryTempDataProvider();
            var state = await StartSync(Build(services, tempData));
            var harness = Build(services, tempData, tokenBody: """{"error":"invalid_grant"}""");

            var result = await harness.Controller.DiscordReturn("code", state);

            Assert.AreEqual("Discord did not return an access token.", ((BadRequestObjectResult)result).Value);
        }

        [TestMethod]
        public void OAuthActionsRequireFullAdminAndTokenNeverTravelsInAQueryString() {
            foreach(var name in new[] { nameof(AdminController.Sync), nameof(AdminController.DiscordReturn) }) {
                var roles = typeof(AdminController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>()!.Roles;
                Assert.AreEqual("Admin,GuildAdmin", roles, name);
            }
            Assert.IsNull(typeof(AdminController).GetMethod("SyncCommandPermissions", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsFalse(typeof(AdminController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(x => x.GetParameters())
                .Any(x => x.Name is "access_token" or "accessToken"));
        }
    }
}
