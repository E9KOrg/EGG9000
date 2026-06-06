using MassTransit;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Common.Consumers {
    public class ExpireCacheConsumer(ILogger<ExpireCacheConsumer> logger, IMemoryCache cache) : IConsumer<ExpireCacheMessage> {
        private readonly ILogger<ExpireCacheConsumer> _logger = logger;
        private readonly IMemoryCache _cache = cache;

        public Task Consume(ConsumeContext<ExpireCacheMessage> context) {
            var assemblyConfigurationAttribute = typeof(ExpireCacheMessage).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;
            _logger.LogInformation("[{build}] Removing cache item for key: {key}", buildConfigurationName, context.Message.Key);
            _cache.Remove(context.Message.Key);
            return Task.CompletedTask;
        }
    }
    public class ExpireCacheMessage(string key) {
        public string Key { get; set; } = key;
    }

}
