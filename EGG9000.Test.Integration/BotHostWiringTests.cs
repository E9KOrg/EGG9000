using EGG9000.Bot;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Integration;

[TestClass]
[TestCategory("Integration")]
public class BotHostWiringTests {
    private static HostBuilderContext FakeContext() {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=localhost;Database=ci;Username=postgres;Password=Doesnt_Matter1",
                ["ConnectionStrings:Token"] = "ci-dummy-token",
            })
            .Build();
        return new HostBuilderContext(new Dictionary<object, object>()) {
            Configuration = config,
            HostingEnvironment = new FakeEnv(),
        };
    }

    private sealed class FakeEnv : IHostEnvironment {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "EGG9000.Bot";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [TestMethod]
    public void BotServices_ConfigureAndResolveCoreServices() {
        var services = new ServiceCollection();
        // The host normally adds logging before ConfigureServices runs; replicate that so the
        // ILoggerFactory lookup inside BotHostFactory resolves.
        services.AddLogging();
        BotHostFactory.ConfigureServices(FakeContext(), services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions {
            ValidateScopes = true,
        });

        Assert.IsNotNull(provider.GetService<IDbContextFactory<ApplicationDbContext>>(),
            "ApplicationDbContext factory should be registered.");

        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        Assert.IsTrue(hostedServices.Count > 0, "Bot should register hosted services.");
    }
}
