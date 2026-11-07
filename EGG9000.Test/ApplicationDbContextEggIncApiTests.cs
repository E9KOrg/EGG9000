using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ApplicationDbContextEggIncApiTests {
        private const string ApiContractId = "api-contract";
        private const string DbContractId = "db-contract";

        public class ArchiveApi : DispatchProxy {
            public int ArchiveCalls;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) {
                if(targetMethod!.Name != nameof(IEggIncApi.GetContractsArchive))
                    throw new NotSupportedException(targetMethod.Name);
                ArchiveCalls++;
                var archive = new Ei.ContractsArchive();
                archive.Archive.Add(new Ei.LocalContract {
                    Contract = new Ei.Contract {
                        Identifier = ApiContractId,
                    },
                });
                return Task.FromResult<ApiResult<Ei.ContractsArchive>>(archive);
            }
        }

        private static (IEggIncApi Api, ArchiveApi Recorder) FakeApi() {
            var api = DispatchProxy.Create<IEggIncApi, ArchiveApi>();
            return (api, (ArchiveApi)(object)api);
        }

        private static async Task<DbContextOptions<ApplicationDbContext>> SeededOptions() {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            await using var seed = new ApplicationDbContext(options);
            var contract = new DBContract {
                ID = DbContractId,
            };
            contract.OverwriteDetails(new Ei.Contract {
                Identifier = DbContractId,
                Name = "Seeded",
            });
            seed.Contracts.Add(contract);
            await seed.SaveChangesAsync();
            return options;
        }

        [TestMethod]
        public async Task WithoutApi_ConstructsAndFallsBackToDbContracts() {
            var options = await SeededOptions();
            await using var db = new ApplicationDbContext(options);

            var contracts = await db.CachedEiContractsAsync();

            CollectionAssert.AreEquivalent(new[] { DbContractId }, contracts.Select(c => c.Identifier).ToList());
        }

        [TestMethod]
        public async Task WithApi_MergesArchiveAndDbContracts() {
            var options = await SeededOptions();
            var (api, recorder) = FakeApi();
            await using var db = new ApplicationDbContext(options, new MemoryCache(new MemoryCacheOptions()), api);

            var contracts = await db.CachedEiContractsAsync();

            Assert.AreEqual(1, recorder.ArchiveCalls);
            CollectionAssert.AreEquivalent(new[] { ApiContractId, DbContractId }, contracts.Select(c => c.Identifier).ToList());
        }

        [TestMethod]
        public async Task ContextFactory_InjectsRegisteredApi() {
            var database = Guid.NewGuid().ToString();
            var (api, recorder) = FakeApi();
            var services = new ServiceCollection();
            services.AddMemoryCache();
            services.AddSingleton(api);
            services.AddDbContextFactory<ApplicationDbContext>(builder => builder.UseInMemoryDatabase(database));
            await using var provider = services.BuildServiceProvider();

            await using var db = await provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContextAsync();
            var contracts = await db.CachedEiContractsAsync();

            Assert.AreEqual(1, recorder.ArchiveCalls);
            CollectionAssert.AreEquivalent(new[] { ApiContractId }, contracts.Select(c => c.Identifier).ToList());
        }

        [TestMethod]
        public async Task ScopedContext_ResolvesWithoutApiRegistered() {
            var services = new ServiceCollection();
            var database = Guid.NewGuid().ToString();
            services.AddDbContext<ApplicationDbContext>(builder => builder.UseInMemoryDatabase(database));
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var contracts = await db.CachedEiContractsAsync();

            Assert.IsEmpty(contracts);
        }
    }
}
