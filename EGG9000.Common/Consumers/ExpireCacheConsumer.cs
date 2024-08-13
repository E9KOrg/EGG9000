using MassTransit;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Common.Consumers {
    public class ExpireCacheConsumer(IHostApplicationLifetime applicationLifetime, ILogger<ExpireCacheConsumer> logger, IMemoryCache cache) : IConsumer<ExpireCacheMessage> {
        private readonly IHostApplicationLifetime _applicationLifetime = applicationLifetime;
        private readonly ILogger<ExpireCacheConsumer> _logger = logger;
        private readonly IMemoryCache _cache = cache;

        public Task Consume(ConsumeContext<ExpireCacheMessage> context) {
            var assemblyConfigurationAttribute = typeof(ExpireCacheMessage).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;

            _cache.Remove(context.Message.Key);
            return Task.CompletedTask;
        }
    }
    public class ExpireCacheMessage {
        public ExpireCacheMessage(String key) {
            Key = key;
        }
        public string Key { get; set; }

    }

}
