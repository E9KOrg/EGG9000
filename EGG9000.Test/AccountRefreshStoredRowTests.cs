using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

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
    public class AccountRefreshStoredRowTests {
        private static DbContextOptions<ApplicationDbContext> Options() {
            return new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        }

        private static DBUser UserWith(params string[] ids) {
            var user = new DBUser { Id = Guid.NewGuid(), DiscordId = 1, DiscordUsername = "u1", GuildId = 1 };
            user.EggIncAccounts = [.. ids.Select(id => new EggIncAccount { Id = id, LastGrade = Ei.Contract.Types.PlayerGrade.GradeA })];
            return user;
        }

        private static Ei.ContractPlayerInfo CompleteAaa() {
            return new() { Grade = Ei.Contract.Types.PlayerGrade.GradeAaa, Status = Ei.ContractPlayerInfo.Types.Status.Complete };
        }

        [TestMethod]
        public async Task StaleFetchDoesNotResurrectAccountRemovedMeanwhile() {
            var options = Options();
            var user = UserWith("EI1", "EI2");
            await using(var seed = new ApplicationDbContext(options)) {
                seed.DBUsers.Add(user);
                await seed.SaveChangesAsync(TestContext!.CancellationToken);
            }

            await using(var live = new ApplicationDbContext(options)) {
                var row = await live.DBUsers.SingleAsync(u => u.Id == user.Id, TestContext.CancellationToken);
                row.EggIncAccounts.RemoveAll(a => a.Id == "EI2");
                row.UpdateAccounts();
                await live.SaveChangesAsync(TestContext.CancellationToken);
            }

            var info = CompleteAaa();
            bool mutated;
            await using(var db = new ApplicationDbContext(options))
                mutated = await AccountRefresh.ApplyExtrasToStoredRowAsync(user.Id, [("EI1", info), ("EI2", info)], db, NullLogger.Instance, TestContext.CancellationToken);

            Assert.IsTrue(mutated);
            await using var check = new ApplicationDbContext(options);
            var stored = await check.DBUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id, TestContext.CancellationToken);
            CollectionAssert.AreEqual(new[] { "EI1" }, stored.EggIncAccounts.Select(a => a.Id).ToList());
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeAaa, stored.EggIncAccounts[0].LastGrade);
        }

        [TestMethod]
        public async Task UnchangedGradeWritesNothing() {
            var options = Options();
            var user = UserWith("EI1");
            await using(var seed = new ApplicationDbContext(options)) {
                seed.DBUsers.Add(user);
                await seed.SaveChangesAsync(TestContext!.CancellationToken);
            }

            var info = new Ei.ContractPlayerInfo { Grade = Ei.Contract.Types.PlayerGrade.GradeA, Status = Ei.ContractPlayerInfo.Types.Status.Complete };
            await using var db = new ApplicationDbContext(options);
            var mutated = await AccountRefresh.ApplyExtrasToStoredRowAsync(user.Id, [("EI1", info)], db, NullLogger.Instance, TestContext.CancellationToken);
            Assert.IsFalse(mutated);
        }

        [TestMethod]
        public async Task MissingUserIsSkipped() {
            await using var db = new ApplicationDbContext(Options());
            var mutated = await AccountRefresh.ApplyExtrasToStoredRowAsync(Guid.NewGuid(), [("EI1", CompleteAaa())], db, NullLogger.Instance, TestContext!.CancellationToken);
            Assert.IsFalse(mutated);
        }

        public TestContext? TestContext { get; set; }
    }
}
