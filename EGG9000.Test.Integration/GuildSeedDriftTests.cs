using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EGG9000.Test.Integration;

[TestClass]
[TestCategory("Integration")]
public class GuildSeedDriftTests {
    // The onboard command seeds a Guild with exactly these three fields and lets EF Core supply
    // every other column from the entity defaults. That is what keeps setup documentation from
    // drifting as the Guilds table grows. If a new NOT NULL column without a default is added to
    // Guild, this test fails instead of a new developer's first setup failing.
    private static DbContextOptions<ApplicationDbContext> Options() {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString, o => o.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
    }

    [TestMethod]
    public async Task Guild_SavesWithOnlyIdAndName() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);

        const ulong id = 998877665544332211UL;
        ctx.Guilds.Add(new Guild { Id = id, DiscordSeverId = id, Name = "Drift Guard Server" });

        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var saved = await ctx.Guilds.SingleAsync(g => g.Id == id, TestContext!.CancellationToken);
        Assert.AreEqual("Drift Guard Server", saved.Name);
        Assert.AreEqual(id, saved.DiscordSeverId);

        ctx.Guilds.Remove(saved);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);
    }

    public TestContext? TestContext { get; set; }
}
