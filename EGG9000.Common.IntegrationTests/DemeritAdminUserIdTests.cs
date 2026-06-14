using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.IntegrationTests;

// Automated demerits historically set AdminUserId = Guid.Empty (a "no admin" sentinel). AdminUserId
// is a nullable FK to DBUser; Guid.Empty references a non-existent user. SQL Server tolerated it,
// Postgres rejects it - the insert throws and, because the Discord message is sent first, the bot
// re-sends forever. ApplicationDbContext.NormalizeAdminUserIds maps the sentinel to null at save.
[TestClass]
[TestCategory("Integration")]
public class DemeritAdminUserIdTests {
    private static DbContextOptions<ApplicationDbContext> Options() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString, o => o.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    [TestMethod]
    public async Task Demerit_WithGuidEmptyAdmin_PersistsAsNull() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync();

        var userId = Guid.NewGuid();
        ctx.DBUsers.Add(new DBUser { Id = userId, DiscordId = 1234500000 + (ulong)Random.Shared.Next(1, 99999), DiscordUsername = "fk-test" });
        await ctx.SaveChangesAsync();

        var demeritId = Guid.NewGuid();
        ctx.Demerit.Add(new Demerit {
            Id = demeritId,
            When = DateTimeOffset.UtcNow,
            UserId = userId,
            AdminUserId = Guid.Empty, // the sentinel that crashed on Postgres
            Reason = "fk-test automated demerit",
        });

        // Before the fix this threw a foreign-key violation. It must now succeed.
        await ctx.SaveChangesAsync();

        var saved = await ctx.Demerit.AsNoTracking().FirstAsync(x => x.Id == demeritId);
        Assert.IsNull(saved.AdminUserId, "Guid.Empty admin sentinel should persist as null, not violate the FK.");
    }
}
