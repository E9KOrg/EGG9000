using EGG9000.Common.Database;
using EGG9000.Common.Helpers;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System.Threading.Tasks;

namespace EGG9000.Common.Consumers {
    public class StorageDictionaryAdoptedConsumer(IDbContextFactory<ApplicationDbContext> factory, ILogger<StorageDictionaryAdoptedConsumer> logger) : IConsumer<StorageDictionaryAdoptedMessage> {
        public async Task Consume(ConsumeContext<StorageDictionaryAdoptedMessage> context) {
            var message = context.Message;
            if(!SecretsHelper.IsValidBusSecret(message.Secret)) {
                logger.LogWarning("Rejected StorageDictionaryAdoptedMessage with invalid control secret");
                return;
            }
            await using var db = await factory.CreateDbContextAsync(context.CancellationToken);
            var row = await db.StorageDictionaries.AsNoTracking().SingleOrDefaultAsync(r => r.Id == message.Id, context.CancellationToken);
            if(row is null) {
                logger.LogWarning("StorageDictionaryAdoptedMessage names unknown dictionary id {Id} ({Corpus})", message.Id, message.Corpus);
                return;
            }
            StorageDictionaryLoader.Apply(row);
            logger.LogInformation("storage dictionary {Id} adopted for {Corpus} (active={Active})", row.Id, row.Corpus, row.Active);
        }
    }

    public class StorageDictionaryAdoptedMessage {
        public int Id { get; set; }
        public string Corpus { get; set; }
        public string Secret { get; set; } = SecretsHelper.BusControlSecret;
    }
}
