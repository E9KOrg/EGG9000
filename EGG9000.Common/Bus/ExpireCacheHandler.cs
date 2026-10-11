using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Bus {
    public sealed record ExpireCacheMessage(string Key);

    public sealed class ExpireCacheHandler(IMemoryCache cache, ILogger<ExpireCacheHandler> logger) : IBusHandler<ExpireCacheMessage> {
        public Task HandleAsync(ExpireCacheMessage message, CancellationToken ct) {
            logger.LogInformation("Removing cache item for key: {key}", message.Key);
            cache.Remove(message.Key);
            return Task.CompletedTask;
        }
    }
}
