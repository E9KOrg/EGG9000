using EGG9000.Common.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public sealed class StorageDictionaryStartup(IDbContextFactory<ApplicationDbContext> factory, ILogger<StorageDictionaryStartup> logger) : IHostedService {
        public Task StartAsync(CancellationToken cancellationToken) {
            using var db = factory.CreateDbContext();
            StorageDictionaryLoader.EnsureLoaded(db);
            logger.LogInformation("storage dictionaries loaded: ids {Ids}, accounts dict {Accounts}, coop status dict {CoopStatus}",
                string.Join(",", StorageDictionary.Registry.Select(d => d.Id)), StorageCompressionStrategy.AccountGraph.DictionaryId, StorageCompressionStrategy.CoopStatus.DictionaryId);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            return Task.CompletedTask;
        }
    }
}
