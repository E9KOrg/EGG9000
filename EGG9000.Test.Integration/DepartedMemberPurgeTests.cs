using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Integration;

[TestClass]
[TestCategory("Integration")]
public class DepartedMemberPurgeTests {
    private static DbContextOptions<ApplicationDbContext> Options() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString, o => o.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    [TestMethod]
    [DataRow(0, 0, false)]   // nobody missing -> never a spike
    [DataRow(100, 0, false)]
    [DataRow(100, 20, false)] // 20% == threshold, not over
    [DataRow(100, 21, true)]  // over 20%
    [DataRow(5, 10, false)]   // small guild, floor of 10 dominates
    [DataRow(5, 11, true)]    // over the floor
    public void DepartureSpikeTooLarge_AppliesFloorAndPercent(int memberCount, int missingCount, bool expected) {
        Assert.AreEqual(expected, ManageOverflow.DepartureSpikeTooLarge(memberCount, missingCount));
    }

    [TestMethod]
    public async Task PendingAssignmentPurgeFilter_SelectsOnlyActiveUnjoinedThisGuild() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        const ulong guildId = 999_000_001;
        const ulong otherGuildId = 999_000_002;

        ctx.DBUsers.Add(new DBUser { Id = userId, DiscordId = 999_111_001, GuildId = 0, DiscordUsername = "departed" });
        ctx.Contracts.Add(new Contract { ID = "test-contract", Created = now });

        var match = NewCoop(guildId, CoopStatusEnum.WaitingOnAssigned, now.AddDays(1));
        var joined = NewCoop(guildId, CoopStatusEnum.WaitingOnAssigned, now.AddDays(1));
        var wrongGuild = NewCoop(otherGuildId, CoopStatusEnum.WaitingOnAssigned, now.AddDays(1));
        var expired = NewCoop(guildId, CoopStatusEnum.WaitingOnAssigned, now.AddDays(-1));
        var full = NewCoop(guildId, CoopStatusEnum.Full, now.AddDays(1));
        ctx.Coops.AddRange(match, joined, wrongGuild, expired, full);

        ctx.UserCoopXrefs.AddRange(
            NewXref(userId, match.Id, joinedCoop: false),
            NewXref(userId, joined.Id, joinedCoop: true),
            NewXref(userId, wrongGuild.Id, joinedCoop: false),
            NewXref(userId, expired.Id, joinedCoop: false),
            NewXref(userId, full.Id, joinedCoop: false));

        await ctx.SaveChangesAsync();

        var matched = await ctx.UserCoopXrefs
            .Where(ManageOverflow.PendingAssignmentPurgeFilter([userId], guildId, now))
            .Select(x => x.CoopId)
            .ToListAsync();

        CollectionAssert.AreEquivalent(new[] { match.Id }, matched,
            "Only the active, unjoined, same-guild assignment should be selected for purge.");
    }

    private static Coop NewCoop(ulong guildId, CoopStatusEnum status, DateTimeOffset ends) => new() {
        Id = Guid.NewGuid(),
        ContractID = "test-contract",
        GuildId = guildId,
        Status = status,
        CoopEnds = ends,
        Created = DateTimeOffset.UtcNow,
        CreatorID = "real"
    };

    private static UserCoopXref NewXref(Guid userId, Guid coopId, bool joinedCoop) => new() {
        UserId = userId,
        CoopId = coopId,
        JoinedCoop = joinedCoop,
        EggIncId = "EI0000000000000000",
        CreatedOn = DateTimeOffset.UtcNow
    };
}
