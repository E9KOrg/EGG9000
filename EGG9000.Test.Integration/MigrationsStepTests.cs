using EGG9000.Onboarding;
using EGG9000.Onboarding.Steps;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Test.Integration;

[TestClass]
[TestCategory("Integration")]
public class MigrationsStepTests {
    private static OnboardContext Context(StringWriter output) {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ApplicationDbContext>(o => o
            .UseNpgsql(PostgresFixture.ConnectionString, x => x.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        var provider = services.BuildServiceProvider();

        return new OnboardContext {
            Configuration = new ConfigurationBuilder().Build(),
            Options = OnboardOptions.Parse(["--onboard"]),
            DbFactory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
            Discord = null!,
            Services = provider,
            Output = output,
            ReadLine = () => null!
        };
    }

    [TestMethod]
    public async Task Run_Twice_SecondRunReportsAlreadyExisted() {
        var output = new StringWriter();
        var ctx = Context(output);

        var first = await new MigrationsStep().RunAsync(ctx, TestContext!.CancellationToken);
        Assert.AreNotEqual(OnboardOutcome.Failed, first.Outcome, first.Detail);

        var second = await new MigrationsStep().RunAsync(ctx, TestContext!.CancellationToken);
        Assert.AreEqual(OnboardOutcome.AlreadyExisted, second.Outcome, second.Detail);
    }

    [TestMethod]
    public async Task Run_LeavesNoPendingMigrations() {
        var output = new StringWriter();
        var ctx = Context(output);
        await new MigrationsStep().RunAsync(ctx, TestContext!.CancellationToken);

        await using var db = await ctx.DbFactory.CreateDbContextAsync(TestContext!.CancellationToken);
        var pending = await db.Database.GetPendingMigrationsAsync(TestContext!.CancellationToken);
        Assert.IsEmpty(pending);
    }

    public TestContext? TestContext { get; set; }
}
