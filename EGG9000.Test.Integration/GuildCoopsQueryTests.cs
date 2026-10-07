using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EGG9000.Test.Integration;

// The guild-coops API key endpoint answers "which co-ops on my server hold at least one member of
// my in-game guild". Guild membership lives in the EggIncAccount.Guild tag inside the packed
// accounts blob, not in a column, so the tag gate cannot run in SQL and has to survive the
// projection the query uses to keep row size down.
[TestClass]
[TestCategory("Integration")]
public class GuildCoopsQueryTests {
    private const string ContractId = "guild-coops-contract";
    private const string GuildTag = "Tachyon";
    private const ulong KeyGuildId = 999_000_020;
    private const ulong OtherGuildId = 999_000_021;

    private static DbContextOptions<ApplicationDbContext> Options() {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString, o => o.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
    }

    [TestMethod]
    public async Task Query_ReturnsEveryPlayerWhenCoopHoldsOneGuildMember() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, KeyGuildId, "e9k-mixed");
        SeedPlayer(ctx, coop, "guildie", GuildTag, joinedCoop: true, starter: true);
        for(var i = 0; i < 7; i++)
            SeedPlayer(ctx, coop, $"rando{i}", guildTag: null, joinedCoop: true);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);

        Assert.HasCount(1, result, "A co-op with one guild member must be returned.");
        Assert.AreEqual("e9k-mixed", result[0].CoopCode);
        Assert.HasCount(8, result[0].Players, "Non-guild players in the co-op must be returned alongside the guild member.");
        Assert.AreEqual(1, result[0].GuildPlayerCount);
        Assert.HasCount(1, result[0].Players.Where(p => p.InGuild).ToList());
    }

    [TestMethod]
    public async Task Query_ExcludesCoopWithNoGuildMembers() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, KeyGuildId, "e9k-nonguild");
        SeedPlayer(ctx, coop, "outsider", guildTag: null);
        SeedPlayer(ctx, coop, "otherguild", "SomeOtherGuild");
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);

        Assert.IsEmpty(result, "A co-op with no members of the key's guild must not be returned.");
    }

    [TestMethod]
    public async Task Query_ExcludesCoopOwnedByAnotherServer() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, OtherGuildId, "e9k-foreign");
        SeedPlayer(ctx, coop, "guildie", GuildTag);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);

        Assert.IsEmpty(result, "A co-op on a different server must not leak through even when it holds guild members.");
    }

    [TestMethod]
    public async Task Query_MatchesGuildOnTheAccountThatJoinedNotAnAlt() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, KeyGuildId, "e9k-alt");
        var userId = Guid.NewGuid();
        var joinedEid = $"EI{Guid.NewGuid():N}"[..18];
        var altEid = $"EI{Guid.NewGuid():N}"[..18];
        ctx.DBUsers.Add(new DBUser {
            Id = userId,
            DiscordId = (ulong)Random.Shared.NextInt64(700_000_000, 799_999_999),
            DiscordUsername = "alt-player",
            EggIncAccounts = [
                new EggIncAccount { Id = joinedEid, Name = "TheAlt", Guild = null },
                new EggIncAccount { Id = altEid, Name = "TheMain", Guild = GuildTag }
            ]
        });
        ctx.UserCoopXrefs.Add(new UserCoopXref {
            UserId = userId,
            CoopId = coop.Id,
            EggIncId = joinedEid,
            FixedUserName = "TheAlt",
            CreatedOn = DateTimeOffset.UtcNow,
            JoinedCoop = true
        });
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);

        Assert.IsEmpty(result, "Joining on an untagged alt must not count the player as a guild member.");
    }

    [TestMethod]
    public async Task Query_MatchesGuildTagIgnoringCaseAndSurroundingWhitespace() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, KeyGuildId, "e9k-casing");
        SeedPlayer(ctx, coop, "sloppytag", "  tachyon ");
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);

        Assert.HasCount(1, result, "Guild tags are user-entered, so casing and padding must not drop a member.");
        Assert.IsTrue(result[0].Players[0].InGuild);
    }

    [TestMethod]
    public async Task Query_ReportsPlayerJoinStateAndStarterSeparately() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, KeyGuildId, "e9k-states");
        SeedPlayer(ctx, coop, "joined-starter", GuildTag, joinedCoop: true, wasAssigned: true, starter: true);
        SeedPlayer(ctx, coop, "assigned", null, wasAssigned: true);
        SeedPlayer(ctx, coop, "kicked", null, joinedCoop: true, wasAssigned: true, removed: true);
        SeedPlayer(ctx, coop, "pending", null);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);
        var byName = result.Single().Players.ToDictionary(p => p.DiscordName, p => p);

        Assert.AreEqual("Joined", byName["joined-starter"].Status);
        Assert.IsTrue(byName["joined-starter"].Starter, "Starter is a separate flag, not a join state.");
        Assert.AreEqual("Assigned", byName["assigned"].Status);
        Assert.AreEqual("Removed", byName["kicked"].Status, "A kick outranks the join it followed.");
        Assert.AreEqual("Pending", byName["pending"].Status);
    }

    [TestMethod]
    public async Task Query_ReturnsDiscordIdAsStringAndNeverTheEggIncId() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, KeyGuildId, "e9k-ids");
        SeedPlayer(ctx, coop, "guildie", GuildTag);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);
        var player = result.Single().Players.Single();

        Assert.IsTrue(ulong.TryParse(player.DiscordId, out _), "Discord ids ship as strings because a ulong overflows a JS number.");
        Assert.IsNull(
            typeof(GuildCoopPlayerApiItem).GetProperty("EggIncId"),
            "The EID is the credential for a player's whole Egg Inc account and must not exist on an API-key-facing DTO.");
    }

    [TestMethod]
    public async Task Query_RedactsDiscordIdOfNonGuildMembers() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, KeyGuildId, "e9k-redact");
        SeedPlayer(ctx, coop, "guildie", GuildTag);
        SeedPlayer(ctx, coop, "outsider", guildTag: null);
        SeedPlayer(ctx, coop, "otherguild", "SomeOtherGuild");
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);
        var byName = result.Single().Players.ToDictionary(p => p.DiscordName, p => p);

        Assert.IsNotNull(byName["guildie"].DiscordId, "Guild members keep their Discord id.");
        Assert.IsNull(byName["outsider"].DiscordId, "A player with no guild must not have their Discord id exposed to the key.");
        Assert.IsNull(byName["otherguild"].DiscordId, "A player in another guild must not have their Discord id exposed to the key.");
    }

    [TestMethod]
    public async Task Query_ExcludesCoopsFromOtherContracts() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);
        var otherContractId = await FreshContractAsync(ctx);

        var wanted = SeedCoop(ctx, contractId, KeyGuildId, "e9k-wanted");
        SeedPlayer(ctx, wanted, "guildie-a", GuildTag);
        var unwanted = SeedCoop(ctx, otherContractId, KeyGuildId, "e9k-unwanted");
        SeedPlayer(ctx, unwanted, "guildie-b", GuildTag);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);

        Assert.HasCount(1, result);
        Assert.AreEqual("e9k-wanted", result[0].CoopCode);
    }

    [TestMethod]
    public async Task Query_ExcludesCoopWhoseOnlyGuildMemberWasKicked() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);

        var coop = SeedCoop(ctx, contractId, KeyGuildId, "e9k-kicked-guildie");
        SeedPlayer(ctx, coop, "kicked-guildie", GuildTag, joinedCoop: true, removed: true);
        SeedPlayer(ctx, coop, "rando", null, joinedCoop: true);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, KeyGuildId, contractId, GuildTag, TestContext!.CancellationToken);

        Assert.IsEmpty(result, "A kicked guild member is no longer in the co-op, so the co-op stops being a guild co-op.");
    }

    [TestMethod]
    public async Task Query_ReturnsBoardingGroupWhenServerHasBgsEnabled() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);
        var guildId = SeedGuild(ctx, disableBg: false);

        var bg2 = SeedCoop(ctx, contractId, guildId, "e9k-bg2", group: 2);
        SeedPlayer(ctx, bg2, "guildie-a", GuildTag);
        var manual = SeedCoop(ctx, contractId, guildId, "e9k-manual", group: 0);
        SeedPlayer(ctx, manual, "guildie-b", GuildTag);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, guildId, contractId, GuildTag, TestContext!.CancellationToken);
        var byCode = result.ToDictionary(c => c.CoopCode, c => c);

        Assert.AreEqual(2, byCode["e9k-bg2"].BoardingGroup);
        Assert.IsNull(byCode["e9k-manual"].BoardingGroup, "A co-op not made by a BG launch has no boarding group to report.");
    }

    [TestMethod]
    public async Task Query_OmitsBoardingGroupWhenServerHasBgsDisabled() {
        await using var ctx = new ApplicationDbContext(Options());
        await ctx.Database.MigrateAsync(TestContext!.CancellationToken);
        var contractId = await FreshContractAsync(ctx);
        var guildId = SeedGuild(ctx, disableBg: true);

        var coop = SeedCoop(ctx, contractId, guildId, "e9k-nobg", group: 1);
        SeedPlayer(ctx, coop, "guildie", GuildTag);
        await ctx.SaveChangesAsync(TestContext!.CancellationToken);

        var result = await GuildCoops.QueryAsync(ctx, guildId, contractId, GuildTag, TestContext!.CancellationToken);

        Assert.IsNull(result.Single().BoardingGroup, "The Group stamp is meaningless on a server with BGs off.");
        var json = System.Text.Json.JsonSerializer.Serialize(result.Single());
        Assert.DoesNotContain("BoardingGroup", json, "With BGs off the field is left out of the response, not sent as null.");
    }

    private static ulong SeedGuild(ApplicationDbContext ctx, bool disableBg) {
        var id = (ulong)Random.Shared.NextInt64(900_000_000, 999_999_999);
        ctx.Guilds.Add(new Guild { Id = id, Name = "bg-test", DisableBG = disableBg });
        return id;
    }

    private static async Task<string> FreshContractAsync(ApplicationDbContext ctx) {
        var id = $"{ContractId}-{Guid.NewGuid():N}"[..32];
        ctx.Contracts.Add(new DBContract { ID = id, Created = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
        return id;
    }

    private static Coop SeedCoop(ApplicationDbContext ctx, string contractId, ulong guildId, string code, ulong group = 0) {
        var coop = new Coop {
            Id = Guid.NewGuid(),
            ContractID = contractId,
            Name = code,
            GuildId = guildId,
            Status = CoopStatus.WaitingOnAssigned,
            CoopEnds = DateTimeOffset.UtcNow.AddDays(1),
            Created = DateTimeOffset.UtcNow,
            CreatorID = "real",
            Group = group
        };
        ctx.Coops.Add(coop);
        return coop;
    }

    private static void SeedPlayer(
        ApplicationDbContext ctx,
        Coop coop,
        string discordUsername,
        string? guildTag,
        bool joinedCoop = false,
        bool wasAssigned = false,
        bool removed = false,
        bool starter = false) {

        var userId = Guid.NewGuid();
        var eid = $"EI{Guid.NewGuid():N}"[..18];
        ctx.DBUsers.Add(new DBUser {
            Id = userId,
            DiscordId = (ulong)Random.Shared.NextInt64(700_000_000, 799_999_999),
            DiscordUsername = discordUsername,
            EggIncAccounts = [new EggIncAccount { Id = eid, Name = $"{discordUsername}-ei", Guild = guildTag }]
        });
        ctx.UserCoopXrefs.Add(new UserCoopXref {
            UserId = userId,
            CoopId = coop.Id,
            EggIncId = eid,
            FixedUserName = $"{discordUsername}-ei",
            CreatedOn = DateTimeOffset.UtcNow,
            JoinedCoop = joinedCoop,
            WasAssigned = wasAssigned,
            Removed = removed,
            RemovedOn = removed ? DateTimeOffset.UtcNow : null,
            Starter = starter
        });
    }

    public TestContext? TestContext { get; set; }
}
