using EGG9000.Common.Database;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Bus {
    public sealed record StorageDictionaryAdoptedMessage(int Id, string Corpus);

    public sealed class StorageDictionaryAdoptedHandler(IDbContextFactory<ApplicationDbContext> factory, ILogger<StorageDictionaryAdoptedHandler> logger) : IBusHandler<StorageDictionaryAdoptedMessage> {
        public async Task HandleAsync(StorageDictionaryAdoptedMessage message, CancellationToken ct) {
            await using var db = await factory.CreateDbContextAsync(ct);
            var row = await db.StorageDictionaries.AsNoTracking().SingleOrDefaultAsync(r => r.Id == message.Id, ct);
            if(row is null) {
                logger.LogWarning("StorageDictionaryAdoptedMessage names unknown dictionary id {Id} ({Corpus})", message.Id, message.Corpus);
                return;
            }
            StorageDictionaryLoader.Apply(row);
            logger.LogInformation("storage dictionary {Id} adopted for {Corpus} (active={Active})", row.Id, row.Corpus, row.Active);
        }
    }
}
