using EGG9000.Onboarding;
using EGG9000.Onboarding.Steps;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EGG9000.Test.Integration;

[TestClass]
[TestCategory("Integration")]
public class AdminRoleStepTests {
    // Builds the minimal Identity registration onboard uses: EF stores over ApplicationDbContext,
    // no cookies, no HTTP. RoleManager and UserManager are scoped, so callers create a scope.
    internal static ServiceProvider BuildProvider() {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContextFactory<ApplicationDbContext>(o => o
            .UseNpgsql(PostgresFixture.ConnectionString, x => x.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddDbContext<ApplicationDbContext>(o => o
            .UseNpgsql(PostgresFixture.ConnectionString, x => x.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    internal static OnboardContext Context(ServiceProvider provider, string[] args) {
        return new OnboardContext {
            Configuration = new ConfigurationBuilder().Build(),
            Options = OnboardOptions.Parse(args),
            DbFactory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
            Discord = null!,
            Services = provider,
            Output = new StringWriter(),
            ReadLine = () => null!
        };
    }

    [TestMethod]
    public async Task Run_CreatesAdminRole_ThenReportsAlreadyExisted() {
        await using var provider = BuildProvider();
        var ctx = Context(provider, ["--onboard"]);

        await using(var db = await ctx.DbFactory.CreateDbContextAsync(TestContext!.CancellationToken)) {
            await db.Database.MigrateAsync(TestContext!.CancellationToken);
        }

        var first = await new AdminRoleStep().RunAsync(ctx, TestContext!.CancellationToken);
        Assert.AreNotEqual(OnboardOutcome.Failed, first.Outcome, first.Detail);

        using(var scope = provider.CreateScope()) {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.IsTrue(await roles.RoleExistsAsync("Admin"));
        }

        var second = await new AdminRoleStep().RunAsync(ctx, TestContext!.CancellationToken);
        Assert.AreEqual(OnboardOutcome.AlreadyExisted, second.Outcome, second.Detail);
    }

    public TestContext? TestContext { get; set; }
}
