using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace EGG9000.Test.Integration;

// The not-joined kick soft-removes the xref instead of deleting it, so anything that reads
// UserCoopXrefs as a live assignment has to filter Removed.
[TestClass]
[TestCategory("Integration")]
public class SoftRemovedXrefQueryTests {
    private const string ContractId = "soft-removed-contract";

    private static DbContextOptions<ApplicationDbContext> Options() {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString, o => o.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
    }

    private sealed class Factory : IDbContextFactory<ApplicationDbContext> {
        public ApplicationDbContext CreateDbContext() => new(Options());
    }

    [TestMethod]
    public async Task AddOrReviveXref_RevivesSoftRemovedRowInsteadOfInserting() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);

        var userId = Guid.NewGuid();
        var coop = await SeedAsync(ctx, userId, removed: true, joinedCoop: false);

        var removedOn = DateTimeOffset.UtcNow.AddHours(-2);
        var existing = await ctx.UserCoopXrefs.FirstAsync(x => x.UserId == userId && x.CoopId == coop.Id, TestContext!.CancellationToken);
        existing.RemovedOn = removedOn;
        existing.JoinWarning12h = true;
        existing.JoinWarning24h = true;
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        await CreateCoopsV2.AddOrReviveXrefAsync(ctx, new UserCoopXref {
            UserId = userId,
            CoopId = coop.Id,
            EggIncId = "EI0000000000000001",
            CreatedOn = DateTimeOffset.UtcNow,
            AddedToChannel = true,
            WasAssigned = true
        });
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var xrefs = await ctx.UserCoopXrefs.Where(x => x.UserId == userId && x.CoopId == coop.Id).ToListAsync(TestContext!.CancellationToken);

        Assert.HasCount(1, xrefs, "Re-placing into a co-op the user was kicked from must not insert a second xref.");
        Assert.IsFalse(xrefs[0].Removed, "Revived xref must no longer be soft-removed.");
        Assert.IsNull(xrefs[0].RemovedOn);
        Assert.IsFalse(xrefs[0].JoinWarning12h, "Join reminders must restart for the new join window.");
        Assert.IsFalse(xrefs[0].JoinWarning24h);
    }

    [TestMethod]
    public async Task AddOrReviveXref_InsertsWhenNoRowExists() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);

        var userId = Guid.NewGuid();
        var coop = await SeedAsync(ctx, userId, removed: false, joinedCoop: false, addXref: false);

        await CreateCoopsV2.AddOrReviveXrefAsync(ctx, new UserCoopXref {
            UserId = userId,
            CoopId = coop.Id,
            EggIncId = "EI0000000000000001",
            CreatedOn = DateTimeOffset.UtcNow,
            AddedToChannel = true,
            WasAssigned = true
        });
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        Assert.HasCount(1, await ctx.UserCoopXrefs.Where(x => x.UserId == userId && x.CoopId == coop.Id).ToListAsync(TestContext!.CancellationToken));
    }

    [TestMethod]
    public async Task LookupRefresh_ExcludesSoftRemovedAssignments() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);

        var removedUserId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        await SeedAsync(ctx, removedUserId, removed: true, joinedCoop: false);
        await SeedAsync(ctx, assignedUserId, removed: false, joinedCoop: false);

        var lookup = new CoopAssignmentLookup(new Factory(), NullLogger<CoopAssignmentLookup>.Instance);
        await lookup.RefreshAsync();

        Assert.IsNull(lookup.Get(removedUserId, ContractId), "Find My Coop must not point a kicked user at the co-op they were removed from.");
        Assert.IsNotNull(lookup.Get(assignedUserId, ContractId), "A normal unjoined assignment must still be found.");
    }

    private static async Task<Coop> SeedAsync(ApplicationDbContext ctx, Guid userId, bool removed, bool joinedCoop, bool addXref = true) {
        if(!await ctx.Contracts.AnyAsync(x => x.ID == ContractId)) {
            ctx.Contracts.Add(new DBContract { ID = ContractId, Created = DateTimeOffset.UtcNow });
        }

        ctx.DBUsers.Add(new DBUser { Id = userId, DiscordId = (ulong)Random.Shared.NextInt64(900_000_000, 999_999_999), GuildId = 0, DiscordUsername = "soft-removed-tester" });

        var coop = new Coop {
            Id = Guid.NewGuid(),
            ContractID = ContractId,
            GuildId = 999_000_010,
            Status = CoopStatusEnum.WaitingOnAssigned,
            CoopEnds = DateTimeOffset.UtcNow.AddDays(1),
            Created = DateTimeOffset.UtcNow,
            CreatorID = "real"
        };
        ctx.Coops.Add(coop);

        if(addXref) {
            ctx.UserCoopXrefs.Add(new UserCoopXref {
                UserId = userId,
                CoopId = coop.Id,
                EggIncId = "EI0000000000000001",
                CreatedOn = DateTimeOffset.UtcNow.AddHours(-20),
                JoinedCoop = joinedCoop,
                Removed = removed,
                RemovedOn = removed ? DateTimeOffset.UtcNow : null
            });
        }

        await ctx.SaveChangesAsync();
        return coop;
    }

    public TestContext? TestContext { get; set; }
}
