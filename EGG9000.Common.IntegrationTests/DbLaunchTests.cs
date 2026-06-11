using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.IntegrationTests;

[TestClass]
[TestCategory("Integration")]
public class DbLaunchTests {
    // This suite verifies the committed migrations apply cleanly to an empty database. Whether the
    // entity model is in sync with those migrations is a separate concern owned by ModelDriftTests,
    // so the PendingModelChangesWarning (which EF promotes to an error during Migrate) is ignored
    // here to keep the two checks independent.
    private static DbContextOptions<ApplicationDbContext> Options() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(SqlServerFixture.ConnectionString, o => o.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    [TestMethod]
    public async Task AllMigrations_ApplyToEmptyDatabase() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync();
        var applied = await ctx.Database.GetAppliedMigrationsAsync();
        Assert.IsTrue(applied.Any(), "Expected at least one migration to be applied.");
        var pending = await ctx.Database.GetPendingMigrationsAsync();
        Assert.AreEqual(0, pending.Count(), "All migrations should be applied after MigrateAsync.");
    }

    [TestMethod]
    public async Task Context_OpensAndQueriesAfterMigrate() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync();
        Assert.IsTrue(await ctx.Database.CanConnectAsync(), "Context should connect to the migrated database.");
        var userCount = await ctx.DBUsers.CountAsync();
        Assert.IsTrue(userCount >= 0);
    }
}
