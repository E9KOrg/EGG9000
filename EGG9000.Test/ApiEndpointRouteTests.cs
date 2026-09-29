using EGG9000.Site.Auth;
using EGG9000.Site.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;

namespace EGG9000.Test {

    // The API-key endpoints are consumed from outside this repo, so their URLs are a contract no
    // refactor gets to change quietly. Adding a [Route] to an action replaces conventional routing
    // for it, which is how a "tidy the paths" change silently 404s every live key holder.
    [TestClass]
    [TestCategory("Unit")]
    public class ApiEndpointRouteTests {

        private static string[] RouteTemplatesFor(string actionName) {
            var method = typeof(APIController).GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, $"APIController.{actionName} does not exist.");
            return [.. method.GetCustomAttributes<RouteAttribute>().Select(a => a.Template)];
        }

        // Route matching is case-insensitive, so the path an action answers is what matters here, not
        // how the template happens to be spelled. Pinning the spelling would turn a harmless casing
        // tidy-up into a red test.
        private static void AssertServesPath(string actionName, string path) {
            var templates = RouteTemplatesFor(actionName);
            Assert.IsTrue(
                templates.Any(t => string.Equals(t, path, StringComparison.OrdinalIgnoreCase)),
                $"APIController.{actionName} does not answer '{path}'. Routes: {string.Join(", ", templates)}");
        }

        [TestMethod]
        public void GuildCoopsJson_IsServedUnderApi() {
            AssertServesPath("GuildCoopsJson", "api/GuildCoopsJson");
        }

        [TestMethod]
        public void LeaderboardJson_IsServedUnderApi() {
            AssertServesPath("LeaderboardJson", "api/LeaderboardJson");
        }

        [TestMethod]
        public void LeaderboardJson_StillAnswersItsOriginalPath() {
            // Keys in the wild were issued against /Home/LeaderboardJson. Dropping this route is a
            // breaking change for every existing consumer, so it has to be a deliberate one.
            AssertServesPath("LeaderboardJson", "Home/LeaderboardJson");
        }

        [TestMethod]
        public void ApiKeyEndpointsAreNotCoveredByAControllerLevelAllowAnonymous() {
            // [AllowAnonymous] on the controller wins over [Authorize] on an action, so one added to
            // APIController would hand every API-key endpoint to the public without failing anything
            // else. The open image endpoints carry their own [AllowAnonymous] instead.
            Assert.AreEqual(
                0,
                typeof(APIController).GetCustomAttributes<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>(inherit: true).Count(),
                "APIController must not be [AllowAnonymous] at the class level; mark the open actions individually.");

            foreach(var action in new[] { "GuildCoopsJson", "LeaderboardJson" }) {
                var method = typeof(APIController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(method, $"APIController.{action} does not exist.");
                Assert.IsTrue(
                    method.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.AuthenticationSchemes == ApiKeyAuthenticationHandler.SchemeName),
                    $"{action} must require the {ApiKeyAuthenticationHandler.SchemeName} scheme.");
            }
        }

        [TestMethod]
        public void NoApiKeyEndpointIsLeftOnConventionalRoutingOnly() {
            foreach(var action in new[] { "GuildCoopsJson", "LeaderboardJson" }) {
                Assert.IsTrue(
                    RouteTemplatesFor(action).Length > 0,
                    $"{action} has no explicit route, so its URL depends on the controller name and can move by rename.");
            }
        }
    }
}
