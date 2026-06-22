using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Integration;

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

    // The exact shape that crashed HomeController.EggDayLeaderboard: a DateTimeOffset literal with a
    // non-UTC offset compared against a DateTime ('date') column, inside a GroupBy + nested Select.
    // The model value converter does NOT reach this parameter - the column is DateTime, not
    // DateTimeOffset, so Properties<DateTimeOffset>().HaveConversion never binds it. Only
    // UtcDateTimeOffsetCommandInterceptor normalizes it; without the interceptor Npgsql throws
    // "Cannot write DateTimeOffset with Offset=-05:00:00 ... only offset 0 (UTC) is supported".
    [TestMethod]
    public async Task LocalOffsetParameter_AgainstDateColumn_DoesNotThrow() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync();

        var eggIncId = "UtcInterceptorTest-" + Guid.NewGuid().ToString("N");
        ctx.UserSnapShots.Add(new UserSnapShot { Date = new DateTime(2026, 7, 10), UserId = Guid.NewGuid(), EggIncID = eggIncId, EarningsBonus = 1 });
        ctx.UserSnapShots.Add(new UserSnapShot { Date = new DateTime(2026, 7, 12), UserId = Guid.NewGuid(), EggIncID = eggIncId, EarningsBonus = 2 });
        await ctx.SaveChangesAsync();

        var cutoff = new DateTimeOffset(2026, 7, 14, 11, 0, 0, TimeSpan.FromHours(-5));
        var ids = new[] { eggIncId };

        var latest = await ctx.UserSnapShots.AsQueryable()
            .Where(x => ids.Contains(x.EggIncID) && x.Date < cutoff)
            .GroupBy(x => x.EggIncID)
            .Select(g => g.OrderByDescending(y => y.Date).First())
            .ToListAsync();

        Assert.AreEqual(1, latest.Count, "The pre-cutoff snapshot query must run without an Npgsql offset error.");
        Assert.AreEqual(new DateTime(2026, 7, 12), latest[0].Date, "Should return the latest snapshot before the cutoff.");
    }
}
