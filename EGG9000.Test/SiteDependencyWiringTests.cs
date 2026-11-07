using Bugsnag.AspNet.Core;

using Discord.WebSocket;

using EGG9000.Common.Bus;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Services;
using EGG9000.Site.Bus;
using EGG9000.Site.Data;
using EGG9000.Site.Services;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class SiteDependencyWiringTests {
        private static readonly Assembly SiteAssembly = typeof(HubMessageBus).Assembly;

        private sealed class FakeWebHostEnvironment : IWebHostEnvironment {
            public string WebRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
            public string EnvironmentName { get; set; } = "Development";
            public string ApplicationName { get; set; } = "EGG9000.Site";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static IServiceCollection SiteServices() {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> {
                    ["ConnectionStrings:DefaultConnection"] = "unused",
                })
                .Build();
            var environment = new FakeWebHostEnvironment();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IWebHostEnvironment>(environment);
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(environment);
            services.AddLogging();

            services.AddDataProtection().PersistKeysToDbContext<ApplicationDbContext>().SetApplicationName("EGG9000");
            var database = Guid.NewGuid().ToString();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(database), contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);
            services.AddDbContextFactory<ApplicationDbContext>(options => options.UseInMemoryDatabase(database));
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders()
                .AddClaimsPrincipalFactory<CustomClaimsPrincipleFactory>();

            services.AddResponseCaching();
            services.AddControllersWithViews().AddXmlSerializerFormatters().AddXmlDataContractSerializerFormatters();
            services.AddRazorPages();
            services.AddHttpClient();
            services.AddEggIncApi();
            services.AddTransient<IEmailSender, EmailSenderBlank>();
            services.AddSingleton<ArtifactImageRenderer>();
            services.AddScoped<LeaderboardService>();
            services.AddHostedService<StorageDictionaryStartup>();
            services.AddHostedService<NewCoopChecker>();
            services.AddSingleton<DatabaseCache>();
            services.AddHostedService<UserCacheRefreshService>();
            services.AddHostedService<ActiveCoopsCacheRefreshService>();

            services.AddSingleton(new DiscordSocketClient());
            services.AddBugsnag();

            services.AddSignalR();
            services.AddSingleton<IMessageBus, HubMessageBus>();
            services.AddSingleton<BotMetricsExporter>();
            services.AddSharedBusHandlers();
            services.AddBusHandler<BotMetricsSnapshotMessage, BotMetricsSnapshotHandler>();
            return services;
        }

        private static ServiceProvider BuildProvider(IServiceCollection services) =>
            services.BuildServiceProvider(new ServiceProviderOptions {
                ValidateScopes = true,
            });

        [TestMethod]
        public void CoreServices_Resolve() {
            using var provider = BuildProvider(SiteServices());
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;

            Assert.IsInstanceOfType<EggIncApi>(services.GetRequiredService<IEggIncApi>());
            Assert.IsNotNull(services.GetRequiredService<IHttpClientFactory>().CreateClient());
            Assert.IsInstanceOfType<HubMessageBus>(services.GetRequiredService<IMessageBus>());
            Assert.IsNotNull(services.GetRequiredService<IMemoryCache>());
            Assert.IsNotNull(services.GetRequiredService<Bugsnag.IClient>());
            Assert.IsNotNull(services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>());
            Assert.IsNotNull(services.GetRequiredService<ApplicationDbContext>());
            Assert.IsNotNull(services.GetRequiredService<LeaderboardService>());
        }

        [TestMethod]
        public void ViewInjectedServices_Resolve() {
            using var provider = BuildProvider(SiteServices());
            using var scope = provider.CreateScope();
            var services = scope.ServiceProvider;

            Assert.IsNotNull(services.GetRequiredService<DiscordSocketClient>());
            Assert.IsNotNull(services.GetRequiredService<ArtifactImageRenderer>());
            Assert.IsNotNull(services.GetRequiredService<SignInManager<ApplicationUser>>());
            Assert.IsNotNull(services.GetRequiredService<UserManager<ApplicationUser>>());
            Assert.IsNotNull(services.GetRequiredService<RoleManager<IdentityRole>>());
        }

        [TestMethod]
        public void EveryController_Constructs() {
            using var provider = BuildProvider(SiteServices());
            using var scope = provider.CreateScope();
            var controllers = DependencyWiring.ConcreteTypes(SiteAssembly, typeof(ControllerBase).IsAssignableFrom);

            Assert.IsTrue(controllers.Any(t => t.Name == "AdminController"), "AdminController was not discovered.");
            DependencyWiring.AssertAllConstruct(scope.ServiceProvider, controllers);
        }

        [TestMethod]
        public void EveryPageModel_Constructs() {
            using var provider = BuildProvider(SiteServices());
            using var scope = provider.CreateScope();
            var pages = DependencyWiring.ConcreteTypes(SiteAssembly, typeof(PageModel).IsAssignableFrom);

            DependencyWiring.AssertAllConstruct(scope.ServiceProvider, pages);
        }

        [TestMethod]
        public void EveryHostedService_Constructs() {
            var services = SiteServices();
            using var provider = BuildProvider(services);

            DependencyWiring.AssertAllConstruct(DependencyWiring.HostedServices(services), DependencyWiring.Describe, d => DependencyWiring.Construct(provider, d));
        }

        [TestMethod]
        public void EveryBusHandler_Resolves() {
            var services = SiteServices();
            using var provider = BuildProvider(services);
            using var scope = provider.CreateScope();

            var handlers = DependencyWiring.BusHandlers(services);
            Assert.Contains(typeof(BotMetricsSnapshotHandler), handlers);
            DependencyWiring.AssertAllConstruct(handlers, t => t.FullName!, t => scope.ServiceProvider.GetRequiredService(t));
            Assert.IsNotNull(scope.ServiceProvider.GetRequiredService<BusDispatcher>());
        }
    }
}
