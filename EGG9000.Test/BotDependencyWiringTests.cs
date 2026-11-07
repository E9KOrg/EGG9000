using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Interactions;
using EGG9000.Bot.Services;
using EGG9000.Common.Bus;
using EGG9000.Common.Database;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class BotDependencyWiringTests {
        private static readonly Assembly BotAssembly = typeof(BotHostFactory).Assembly;
        private static readonly Assembly CommonAssembly = typeof(ApplicationDbContext).Assembly;

        private sealed class WiredHost(IHost host, IServiceCollection descriptors) : IDisposable {
            public IServiceProvider Services => host.Services;
            public IServiceCollection Descriptors => descriptors;
            public void Dispose() => host.Dispose();
        }

        private static readonly Lazy<WiredHost> SharedHost = new(BuildHost);

        [ClassCleanup]
        public static void DisposeHost() {
            if(SharedHost.IsValueCreated) SharedHost.Value.Dispose();
        }

        private static WiredHost BuildHost() {
            IServiceCollection? registered = null;
            var host = new HostBuilder()
                .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=ci;Username=postgres;Password=unused",
                    ["ConnectionStrings:ApiSalt"] = "unused",
                }))
                .UseDefaultServiceProvider(options => options.ValidateScopes = false)
                .ConfigureServices(BotHostFactory.ConfigureServices)
                .ConfigureServices((_, services) => {
                    ReplaceExternalDependencies(services);
                    registered = services;
                })
                .Build();
            return new WiredHost(host, registered!);
        }

        private static void ReplaceExternalDependencies(IServiceCollection services) {
            DependencyWiring.UseInMemoryDatabase(services, Guid.NewGuid().ToString());
            var gateway = new DiscordSocketClient();
            var discord = (DiscordHostedService)RuntimeHelpers.GetUninitializedObject(typeof(DiscordHostedService));
            typeof(DiscordHostedService).GetField("_gateway", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(discord, gateway);
            Replace(services, discord);
            Replace(services, gateway);
            Replace(services, new InteractionService(gateway, new InteractionServiceConfig {
                DefaultRunMode = RunMode.Sync,
            }));
        }

        private static void Replace<T>(IServiceCollection services, T instance) where T : class {
            services.RemoveAll<T>();
            services.AddSingleton(instance);
        }

        private static bool IsFirstParty(Type type) =>
            DependencyWiring.IsFirstParty(type) || type.Assembly == typeof(EggSharp.Api.IEggIncApi).Assembly;

        [TestMethod]
        public void CoreServices_Resolve() {
            var host = SharedHost.Value;
            var provider = host.Services;

            var api = provider.GetRequiredService<IEggIncApi>();
            Assert.IsInstanceOfType<EggIncApi>(api);
            Assert.AreSame(api, provider.GetRequiredService<IEggIncApi>());
            Assert.IsNotNull(provider.GetRequiredService<EggSharp.Api.IEggIncApi>());
            Assert.IsNotNull(provider.GetRequiredService<IHttpClientFactory>());
            Assert.IsNotNull(provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>());
            Assert.IsNotNull(provider.GetRequiredService<IMessageBus>());
            Assert.IsNotNull(provider.GetRequiredService<IDiscordQueue>());
        }

        [TestMethod]
        public void EggSharpNamedClients_Resolve() {
            var host = SharedHost.Value;
            var factory = host.Services.GetRequiredService<IHttpClientFactory>();
            foreach(var profile in EggSharp.Api.ApiPresets.Egg9000.Profiles) {
                using var client = factory.CreateClient(EggIncApiHttpClientTests.ClientName(profile));
                Assert.AreEqual(profile.Timeout, client.Timeout, profile.Name);
            }
        }

        [TestMethod]
        public void EveryFirstPartyRegistration_Resolves() {
            var host = SharedHost.Value;
            using var scope = host.Services.CreateScope();
            var services = host.Descriptors
                .Where(d => !d.IsKeyedService && !d.ServiceType.ContainsGenericParameters && IsFirstParty(d.ServiceType))
                .Select(d => d.ServiceType)
                .Distinct()
                .ToList();
            DependencyWiring.AssertAllConstruct(services, t => t.FullName!, t => scope.ServiceProvider.GetRequiredService(t));
        }

        [TestMethod]
        public void EveryHostedService_Constructs() {
            var host = SharedHost.Value;
            var hosted = DependencyWiring.HostedServices(host.Descriptors);
            DependencyWiring.AssertAllConstruct(hosted, DependencyWiring.Describe, d => DependencyWiring.Construct(host.Services, d));
        }

        [TestMethod]
        public void EveryUpdater_Constructs() {
            var host = SharedHost.Value;
            var updaters = DependencyWiring.ConcreteTypes(BotAssembly, t => DependencyWiring.DerivesFromGeneric(t, typeof(_UpdaterBase<>)));
            DependencyWiring.AssertAllConstruct(host.Services, updaters);
        }

        [TestMethod]
        public void EveryInteractionModule_Constructs() {
            var host = SharedHost.Value;
            var modules = DependencyWiring.ConcreteTypes(BotAssembly, t => t.IsVisible && typeof(IInteractionModuleBase).IsAssignableFrom(t));
            Assert.IsTrue(modules.Any(t => t.IsSubclassOf(typeof(E9KModuleBase))), "No E9KModuleBase modules discovered.");
            DependencyWiring.AssertInteractionTypesResolve(host.Services, modules);
        }

        [TestMethod]
        public void EveryAutocompleteHandler_Constructs() {
            var host = SharedHost.Value;
            var handlers = DependencyWiring.ConcreteTypes(BotAssembly, typeof(AutocompleteHandler).IsAssignableFrom);
            DependencyWiring.AssertInteractionTypesResolve(host.Services, handlers);
        }

        [TestMethod]
        public void EveryJobClass_Constructs() {
            var host = SharedHost.Value;
            var jobs = new[] { BotAssembly, CommonAssembly }
                .SelectMany(a => DependencyWiring.ConcreteTypes(a, t => t.IsVisible && t.GetMethods().Any(m => m.GetCustomAttribute<JobAttribute>() is not null)))
                .ToList();
            DependencyWiring.AssertAllConstruct(host.Services, jobs);
        }

        [TestMethod]
        public void EveryBusHandler_Resolves() {
            var host = SharedHost.Value;
            using var scope = host.Services.CreateScope();
            var handlers = DependencyWiring.BusHandlers(host.Descriptors);
            Assert.Contains(typeof(RestartHandler), handlers);
            DependencyWiring.AssertAllConstruct(handlers, t => t.FullName!, t => scope.ServiceProvider.GetRequiredService(t));
        }
    }
}
