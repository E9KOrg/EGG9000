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
public class GuildRowStepTests {
    private const ulong TestGuildId = 778899001122334455UL;

    private static OnboardContext Context() {
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
            Output = new StringWriter(),
            ReadLine = () => null!,
            SelectedGuildId = TestGuildId,
            SelectedGuildName = "Row Step Server"
        };
    }

    [TestInitialize]
    public async Task Setup() {
        var ctx = Context();
        await using var db = await ctx.DbFactory.CreateDbContextAsync(TestContext!.CancellationToken);
        await db.Database.MigrateAsync(TestContext!.CancellationToken);
        await db.Guilds.Where(g => g.Id == TestGuildId).ExecuteDeleteAsync(TestContext!.CancellationToken);
    }

    [TestMethod]
    public async Task Run_NoExistingRow_CreatesWithThreeFields() {
        var ctx = Context();
        var result = await new GuildRowStep().RunAsync(ctx, TestContext!.CancellationToken);

        Assert.AreEqual(OnboardOutcome.Created, result.Outcome, result.Detail);

        await using var db = await ctx.DbFactory.CreateDbContextAsync(TestContext!.CancellationToken);
        var saved = await db.Guilds.SingleAsync(g => g.Id == TestGuildId, TestContext!.CancellationToken);
        Assert.AreEqual("Row Step Server", saved.Name);
        Assert.AreEqual(TestGuildId, saved.DiscordSeverId);
    }

    [TestMethod]
    public async Task Run_Twice_SecondRunReportsAlreadyExisted() {
        var ctx = Context();
        await new GuildRowStep().RunAsync(ctx, TestContext!.CancellationToken);
        var second = await new GuildRowStep().RunAsync(ctx, TestContext!.CancellationToken);
        Assert.AreEqual(OnboardOutcome.AlreadyExisted, second.Outcome, second.Detail);
    }

    [TestMethod]
    public async Task Run_ExistingRow_DoesNotOverwriteOperatorConfiguration() {
        var ctx = Context();
        await new GuildRowStep().RunAsync(ctx, TestContext!.CancellationToken);

        await using(var db = await ctx.DbFactory.CreateDbContextAsync(TestContext!.CancellationToken)) {
            var existing = await db.Guilds.SingleAsync(g => g.Id == TestGuildId, TestContext!.CancellationToken);
            existing.Name = "Renamed By Operator";
            existing.CoopNamePrefix = "prefix-set-in-admin-ui";
            await db.SaveChangesAsync(TestContext!.CancellationToken);
        }

        await new GuildRowStep().RunAsync(ctx, TestContext!.CancellationToken);

        await using(var db = await ctx.DbFactory.CreateDbContextAsync(TestContext!.CancellationToken)) {
            var after = await db.Guilds.SingleAsync(g => g.Id == TestGuildId, TestContext!.CancellationToken);
            Assert.AreEqual("Renamed By Operator", after.Name);
            Assert.AreEqual("prefix-set-in-admin-ui", after.CoopNamePrefix);
        }
    }

    public TestContext? TestContext { get; set; }
}
