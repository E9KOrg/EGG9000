using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class RemovedAccountsSweepTests {
        private static DbContextOptions<ApplicationDbContext> Options() {
            return new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        }

        private static DBUser UserWith(ulong discordId, params string[] ids) {
            var user = new DBUser { Id = Guid.NewGuid(), DiscordId = discordId, DiscordUsername = $"u{discordId}", GuildId = 1 };
            user.EggIncAccounts = [.. ids.Select(id => new EggIncAccount { Id = id })];
            return user;
        }

        private static async Task<List<string>> StoredIds(DbContextOptions<ApplicationDbContext> options, Guid userId) {
            await using var db = new ApplicationDbContext(options);
            var user = await db.DBUsers.AsNoTracking().SingleAsync(u => u.Id == userId);
            return [.. user.EggIncAccounts.Select(a => a.Id)];
        }

        [TestMethod]
        public async Task ReappearedAccountIsRemovedAgainAndCounted() {
            var options = Options();
            var user = UserWith(1, "EI1", "EI2");
            await using(var seed = new ApplicationDbContext(options)) {
                seed.DBUsers.Add(user);
                seed.RemovedAccounts.Add(new RemovedAccount { UserId = user.Id, EggIncId = "EI2", RemovedOn = DateTimeOffset.UtcNow, RemovedByDiscordId = 9 });
                await seed.SaveChangesAsync(TestContext!.CancellationToken);
            }

            List<RemovedAccountsSweep.Reapplied> hits;
            await using(var db = new ApplicationDbContext(options))
                hits = await RemovedAccountsSweep.ReapplyRemovalsAsync(db, NullLogger.Instance, TestContext.CancellationToken);

            Assert.AreEqual(1, hits.Count);
            Assert.AreEqual("EI2", hits[0].EggIncId);
            Assert.AreEqual(1UL, hits[0].DiscordId);
            Assert.AreEqual(1, hits[0].Count);
            CollectionAssert.AreEqual(new[] { "EI1" }, await StoredIds(options, user.Id));

            await using(var again = new ApplicationDbContext(options)) {
                var second = await RemovedAccountsSweep.ReapplyRemovalsAsync(again, NullLogger.Instance, TestContext.CancellationToken);
                Assert.AreEqual(0, second.Count);
                var tombstone = await again.RemovedAccounts.SingleAsync(TestContext.CancellationToken);
                Assert.AreEqual(1, tombstone.ReappliedCount);
                Assert.IsNotNull(tombstone.LastReappliedOn);
            }
        }

        [TestMethod]
        public async Task RemovalOnOtherUserLeavesThisOneAlone() {
            var options = Options();
            var keeper = UserWith(1, "EI1");
            var other = UserWith(2, "EI2");
            await using(var seed = new ApplicationDbContext(options)) {
                seed.DBUsers.AddRange(keeper, other);
                seed.RemovedAccounts.Add(new RemovedAccount { UserId = other.Id, EggIncId = "EI1", RemovedOn = DateTimeOffset.UtcNow, RemovedByDiscordId = 9 });
                await seed.SaveChangesAsync(TestContext!.CancellationToken);
            }

            await using(var db = new ApplicationDbContext(options)) {
                var hits = await RemovedAccountsSweep.ReapplyRemovalsAsync(db, NullLogger.Instance, TestContext.CancellationToken);
                Assert.AreEqual(0, hits.Count);
            }
            CollectionAssert.AreEqual(new[] { "EI1" }, await StoredIds(options, keeper.Id));
        }

        [TestMethod]
        public async Task DuplicateOwnersAreReportedOnlyForSharedIds() {
            var options = Options();
            var a = UserWith(1, "EI1", "EI9");
            var b = UserWith(2, "EI2", "ei9");
            var c = UserWith(3, "EI3");
            await using(var seed = new ApplicationDbContext(options)) {
                seed.DBUsers.AddRange(a, b, c);
                await seed.SaveChangesAsync(TestContext!.CancellationToken);
            }

            await using var db = new ApplicationDbContext(options);
            var duplicates = await RemovedAccountsSweep.FindDuplicateOwnersAsync(db, TestContext.CancellationToken);

            Assert.AreEqual(1, duplicates.Count);
            var owners = duplicates["EI9"];
            CollectionAssert.AreEquivalent(new[] { 1UL, 2UL }, owners.Select(o => o.DiscordId).ToList());
        }

        public TestContext? TestContext { get; set; }
    }
}
