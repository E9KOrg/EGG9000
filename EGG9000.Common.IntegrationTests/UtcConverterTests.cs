using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.IntegrationTests;

// Guards the model-level UTC converter (ApplicationDbContext.ConfigureConventions). Npgsql's
// 'timestamp with time zone' rejects any DateTimeOffset whose offset is not 0. Without the
// converter, writing or querying with a local-offset value (e.g. DateTimeOffset.Now) throws
// "Cannot write DateTimeOffset with Offset=... only offset 0 (UTC) is supported".
[TestClass]
[TestCategory("Integration")]
public class UtcConverterTests {
    private static DbContextOptions<ApplicationDbContext> Options() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString, o => o.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    [TestMethod]
    public async Task LocalOffsetDateTimeOffset_WritesAndQueries() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync();

        // A deliberately non-UTC offset - the exact shape that crashed CoopAssignmentLookup.
        var localNow = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.FromHours(-5));

        ctx.AutomationLogs.Add(new AutomationLog {
            StartTime = localNow,
            Type = "UtcConverterTest",
            Skipped = false,
        });
        await ctx.SaveChangesAsync();

        // Query with a local-offset parameter - EF must route it through the converter.
        // Both the write above and this query would throw the Npgsql offset error without it.
        var cutoff = localNow.AddMinutes(-1);
        var found = await ctx.AutomationLogs
            .Where(x => x.Type == "UtcConverterTest" && x.StartTime > cutoff)
            .ToListAsync();

        Assert.IsTrue(found.Count > 0, "Row written with a local offset should be queryable by a local-offset parameter.");
        // Instant must round-trip exactly (DateTimeOffset equality compares the instant, not the offset).
        Assert.AreEqual(localNow.ToUniversalTime(), found[0].StartTime.ToUniversalTime(), "The instant must be preserved.");
    }
}
