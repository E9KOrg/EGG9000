using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace EGG9000.Test.Integration;

[TestClass]
[TestCategory("Integration")]
public class StorageSweepTests {
    private const string ContractId = "storage-sweep-contract";
    private static readonly byte[] CorruptAccounts = [0xC1, 0xFF, 0x00];
    private static readonly byte[] CorruptCoopStatus = [0x00, 0x01];

    private static DbContextOptions<ApplicationDbContext> Options() {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(PostgresFixture.ConnectionString, o => o.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
    }

    private static ServiceProvider BuildProvider() {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o
            .UseNpgsql(PostgresFixture.ConnectionString, n => n.MigrationsAssembly("EGG9000.Common"))
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        return services.BuildServiceProvider();
    }

    private static byte[] LegacyAccountBytes(params string[] ids) {
        var accounts = ids.Select(id => new EggIncAccount { Id = id, Name = "Sweep " + id }).ToList();
        return MessagePackSerializer.Serialize(accounts, StorageMessagePack.Options);
    }

    private static Ei.ContractCoopStatusResponse SampleStatus(string coopId) {
        var status = new Ei.ContractCoopStatusResponse {
            ContractIdentifier = ContractId,
            CoopIdentifier = coopId,
            TotalAmount = 250_000,
            SecondsRemaining = 7200
        };
        status.Contributors.Add(new Ei.ContractCoopStatusResponse.Types.ContributionInfo { UserId = "EI1", UserName = "One", ContributionAmount = 100_000 });
        status.Contributors.Add(new Ei.ContractCoopStatusResponse.Types.ContributionInfo { UserId = "EI2", UserName = "Two", ContributionAmount = 150_000 });
        return status;
    }

    private static async Task WithWriteFlagsAsync(bool enabled, Func<Task> action) {
        var priorCompress = StorageCodec.CompressWriteEnabled;
        var priorProto = CoopStatusCodec.ProtoWriteEnabled;
        StorageCodec.CompressWriteEnabled = enabled;
        CoopStatusCodec.ProtoWriteEnabled = enabled;
        try {
            await action();
        } finally {
            StorageCodec.CompressWriteEnabled = priorCompress;
            CoopStatusCodec.ProtoWriteEnabled = priorProto;
        }
    }

    private static async Task RunSweepAsync(CancellationToken token) {
        var priorEnabled = Environment.GetEnvironmentVariable(StorageSweepOptions.EnabledVariable);
        var priorDelay = Environment.GetEnvironmentVariable(StorageSweepOptions.BatchDelayVariable);
        Environment.SetEnvironmentVariable(StorageSweepOptions.EnabledVariable, "1");
        Environment.SetEnvironmentVariable(StorageSweepOptions.BatchDelayVariable, "0");
        try {
            await WithWriteFlagsAsync(true, async () => {
                await using var provider = BuildProvider();
                var sweep = new StorageSweep(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StorageSweep>.Instance);
                await sweep.RunOnceAsync(token);
            });
        } finally {
            Environment.SetEnvironmentVariable(StorageSweepOptions.EnabledVariable, priorEnabled);
            Environment.SetEnvironmentVariable(StorageSweepOptions.BatchDelayVariable, priorDelay);
        }
    }

    private static DBUser NewUser(ulong discordId, byte[] blob) {
        return new DBUser { Id = Guid.NewGuid(), DiscordId = discordId, GuildId = 0, DiscordUsername = "sweep-" + discordId, _contractRegistrationByte = blob };
    }

    private static Coop NewCoop(byte[] blob) {
        return new Coop {
            Id = Guid.NewGuid(),
            ContractID = ContractId,
            GuildId = 998_000_001,
            Status = CoopStatus.Full,
            CoopEnds = DateTimeOffset.UtcNow.AddDays(1),
            Created = DateTimeOffset.UtcNow,
            CreatorID = "real",
            _StatusCompressed = blob
        };
    }

    private static async Task EnsureContractAsync(ApplicationDbContext ctx, CancellationToken token) {
        if(!await ctx.Contracts.AnyAsync(c => c.ID == ContractId, token)) {
            ctx.Contracts.Add(new DBContract { ID = ContractId, Created = DateTimeOffset.UtcNow });
            await ctx.SaveChangesAsync(token);
        }
    }

    private static async Task<byte[]> UserBlobAsync(Guid id, CancellationToken token) {
        await using var ctx = new ApplicationDbContext(Options());
        return await ctx.DBUsers.AsNoTracking().Where(u => u.Id == id).Select(u => u._contractRegistrationByte).SingleAsync(token);
    }

    private static async Task<byte[]> CoopBlobAsync(Guid id, CancellationToken token) {
        await using var ctx = new ApplicationDbContext(Options());
        return await ctx.Coops.AsNoTracking().Where(c => c.Id == id).Select(c => c._StatusCompressed).SingleAsync(token);
    }

    private static async Task OverwriteUserBlobAsync(Guid id, byte[] blob, CancellationToken token) {
        await using var ctx = new ApplicationDbContext(Options());
        var user = await ctx.DBUsers.SingleAsync(u => u.Id == id, token);
        user._contractRegistrationByte = blob;
        await ctx.SaveChangesAsync(token);
    }

    [TestMethod]
    public async Task Sweep_ConvertsLegacyRows_LeavesCorruptRowsAlone_LogsRun() {
        var token = TestContext!.CancellationToken;
        var legacyOne = LegacyAccountBytes("EI0000000000000001");
        var legacyTwo = LegacyAccountBytes("EI0000000000000002");
        var coopOne = await WithFlagsOffAsync(() => CoopStatusCodec.Encode(SampleStatus("sweep-one")));
        var coopTwo = await WithFlagsOffAsync(() => CoopStatusCodec.Encode(SampleStatus("sweep-two")));

        var userOne = NewUser(998_111_001, legacyOne);
        var userTwo = NewUser(998_111_002, legacyTwo);
        var userBad = NewUser(998_111_003, CorruptAccounts);
        var coopA = NewCoop(coopOne);
        var coopB = NewCoop(coopTwo);
        var coopBad = NewCoop(CorruptCoopStatus);

        await using(var ctx = new ApplicationDbContext(Options())) {
            await ctx.Database.MigrateAsync(token);
            await EnsureContractAsync(ctx, token);
            ctx.DBUsers.AddRange(userOne, userTwo, userBad);
            ctx.Coops.AddRange(coopA, coopB, coopBad);
            await ctx.SaveChangesAsync(token);
        }

        Assert.AreNotEqual(StorageCompression.Marker, (await UserBlobAsync(userOne.Id, token))[0]);
        Assert.AreNotEqual(StorageCompression.Marker, (await CoopBlobAsync(coopA.Id, token))[0]);

        await RunSweepAsync(token);

        foreach(var (id, expectedAccount) in new[] { (userOne.Id, "EI0000000000000001"), (userTwo.Id, "EI0000000000000002") }) {
            var blob = await UserBlobAsync(id, token);
            Assert.AreEqual(StorageCompression.Marker, blob[0]);
            var accounts = StorageCodec.Unpack<List<EggIncAccount>>(blob);
            Assert.AreEqual(expectedAccount, accounts.Single().Id);
        }
        foreach(var (id, coopId) in new[] { (coopA.Id, "sweep-one"), (coopB.Id, "sweep-two") }) {
            var blob = await CoopBlobAsync(id, token);
            Assert.AreEqual(StorageCompression.Marker, blob[0]);
            var status = CoopStatusCodec.Decode(blob);
            Assert.AreEqual(coopId, status.CoopIdentifier);
            Assert.AreEqual(2, status.Contributors.Count);
            Assert.AreEqual(7200d, status.SecondsRemaining);
        }
        CollectionAssert.AreEqual(CorruptAccounts, await UserBlobAsync(userBad.Id, token));
        CollectionAssert.AreEqual(CorruptCoopStatus, await CoopBlobAsync(coopBad.Id, token));

        await using(var ctx = new ApplicationDbContext(Options())) {
            Assert.IsTrue(await ctx.AutomationLogs.AnyAsync(l => l.Type == StorageSweep.AutomationLogType && l.EndTime != null, token));
        }
    }

    [TestMethod]
    public async Task Sweep_SecondRun_ConvertsRowRewrittenToLegacyBetweenRuns() {
        var token = TestContext!.CancellationToken;
        var user = NewUser(998_222_001, LegacyAccountBytes("EI0000000000000011"));

        await using(var ctx = new ApplicationDbContext(Options())) {
            await ctx.Database.MigrateAsync(token);
            ctx.DBUsers.Add(user);
            await ctx.SaveChangesAsync(token);
        }

        await RunSweepAsync(token);
        Assert.AreEqual(StorageCompression.Marker, (await UserBlobAsync(user.Id, token))[0]);

        var rewritten = LegacyAccountBytes("EI0000000000000011", "EI0000000000000012");
        await OverwriteUserBlobAsync(user.Id, rewritten, token);
        Assert.AreNotEqual(StorageCompression.Marker, (await UserBlobAsync(user.Id, token))[0]);

        await RunSweepAsync(token);

        var blob = await UserBlobAsync(user.Id, token);
        Assert.AreEqual(StorageCompression.Marker, blob[0]);
        var accounts = StorageCodec.Unpack<List<EggIncAccount>>(blob);
        CollectionAssert.AreEqual(new[] { "EI0000000000000011", "EI0000000000000012" }, accounts.Select(a => a.Id).ToList());
    }

    [TestMethod]
    public async Task CasUpdate_StaleOldBytes_AffectsNoRows() {
        var token = TestContext!.CancellationToken;
        var stored = LegacyAccountBytes("EI0000000000000021");
        var user = NewUser(998_333_001, stored);

        await using(var ctx = new ApplicationDbContext(Options())) {
            await ctx.Database.MigrateAsync(token);
            ctx.DBUsers.Add(user);
            await ctx.SaveChangesAsync(token);
        }

        SweepOutcome outcome = default;
        await WithWriteFlagsAsync(true, () => {
            outcome = StorageSweepCodec.Accounts(stored);
            return Task.CompletedTask;
        });
        Assert.AreEqual(SweepOutcomeKind.Converted, outcome.Kind);

        var other = LegacyAccountBytes("EI0000000000000021", "EI0000000000000022");
        await OverwriteUserBlobAsync(user.Id, other, token);

        int affected;
        await using(var ctx = new ApplicationDbContext(Options())) {
            affected = await ctx.Database.ExecuteSqlRawAsync(StorageSweep.UsersCasUpdateSql,
                [new NpgsqlParameter("new", outcome.Bytes), new NpgsqlParameter("id", user.Id), new NpgsqlParameter("old", stored)],
                token);
        }

        Assert.AreEqual(0, affected);
        CollectionAssert.AreEqual(other, await UserBlobAsync(user.Id, token));
    }

    private static async Task<byte[]> WithFlagsOffAsync(Func<byte[]> encode) {
        byte[] result = [];
        await WithWriteFlagsAsync(false, () => {
            result = encode();
            return Task.CompletedTask;
        });
        return result;
    }

    public TestContext? TestContext { get; set; }
}
