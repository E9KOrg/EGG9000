using EGG9000.Common.EggIncAPI;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class EggIncApiHttpClientTests {
        internal static string ClientName(EggSharp.Api.HeaderProfile profile) => "EggSharp.Api." + profile.Name;

        public static IEnumerable<object[]> Profiles => EggSharp.Api.ApiPresets.Egg9000.Profiles.Select(p => new object[] { p.Name });

        private static ServiceProvider BuildProvider() {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddEggIncApi();
            return services.BuildServiceProvider(new ServiceProviderOptions {
                ValidateScopes = true,
            });
        }

        private static EggSharp.Api.HeaderProfile Profile(string name) => EggSharp.Api.ApiPresets.Egg9000.Profiles.Single(p => p.Name == name);

        [TestMethod]
        public void AddEggIncApi_ResolvesAdapterOverEggSharpSingleton() {
            using var provider = BuildProvider();

            var adapter = provider.GetRequiredService<IEggIncApi>();

            Assert.IsInstanceOfType<EggIncApi>(adapter);
            Assert.AreSame(adapter, provider.GetRequiredService<IEggIncApi>());
            Assert.AreSame(provider.GetRequiredService<EggSharp.Api.IEggIncApi>(), provider.GetRequiredService<EggSharp.Api.IEggIncApi>());
            Assert.IsNotNull(provider.GetRequiredService<EggSharp.Api.IEggIncApiMetrics>());
            Assert.IsNotNull(provider.GetRequiredService<IHttpClientFactory>());
        }

        [TestMethod]
        public void Egg9000Preset_HasEveryProfileTheEndpointTableUses() {
            var names = EggSharp.Api.ApiPresets.Egg9000.Profiles.Select(p => p.Name).ToList();

            CollectionAssert.AllItemsAreUnique(names);
            CollectionAssert.IsSubsetOf(new[] { EggSharp.Api.ApiPresets.AndroidProfile, EggSharp.Api.ApiPresets.IosProfile, EggSharp.Api.ApiPresets.CoopStatusProfile }, names);
        }

        [TestMethod]
        [DynamicData(nameof(Profiles))]
        public void NamedClient_AppliesProfileTimeoutVersionAndHeaders(string name) {
            var profile = Profile(name);
            using var provider = BuildProvider();

            using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName(profile));

            Assert.AreEqual(TimeSpan.FromSeconds(30), client.Timeout);
            Assert.AreEqual(profile.Timeout, client.Timeout);
            Assert.AreEqual(profile.HttpVersion, client.DefaultRequestVersion);
            Assert.IsNull(client.BaseAddress);
            Assert.AreEqual(profile.UserAgent is not null, client.DefaultRequestHeaders.Contains("User-Agent"));
            Assert.AreEqual(profile.Cookie is not null, client.DefaultRequestHeaders.Contains("Cookie"));
            foreach(var header in profile.Headers.Keys)
                Assert.IsTrue(client.DefaultRequestHeaders.Contains(header), header);
        }

        [TestMethod]
        [DynamicData(nameof(Profiles))]
        public void NamedClient_PrimaryHandlerDecompressesAndRecyclesConnections(string name) {
            var profile = Profile(name);
            using var provider = BuildProvider();
            var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(ClientName(profile));
            var builder = provider.GetRequiredService<HttpMessageHandlerBuilder>();

            foreach(var action in options.HttpMessageHandlerBuilderActions)
                action(builder);

            var handler = builder.PrimaryHandler as SocketsHttpHandler;
            Assert.IsNotNull(handler, "Egg9000 profiles should install a SocketsHttpHandler as the primary handler.");
            Assert.AreEqual(DecompressionMethods.GZip | DecompressionMethods.Deflate, handler.AutomaticDecompression);
            Assert.AreEqual(TimeSpan.FromMinutes(2), handler.PooledConnectionLifetime);
            Assert.AreEqual(profile.PooledConnectionLifetime, (TimeSpan?)handler.PooledConnectionLifetime);
            Assert.AreEqual(TimeSpan.FromMinutes(2), options.HandlerLifetime);
        }

        [TestMethod]
        public void UnnamedClient_DoesNotInheritEggIncProfile() {
            using var provider = BuildProvider();

            using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient();

            Assert.AreEqual(TimeSpan.FromSeconds(100), client.Timeout);
            Assert.IsFalse(client.DefaultRequestHeaders.Contains("User-Agent"));
        }
    }
}
