using MassTransit;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Common.Consumers {
    public class ShutdownConsumer(IHostApplicationLifetime applicationLifetime, ILogger<ShutdownConsumer> logger) : IConsumer<ShutdownMessage> {
        private readonly IHostApplicationLifetime _applicationLifetime = applicationLifetime;
        private readonly ILogger<ShutdownConsumer> _logger = logger;

        public Task Consume(ConsumeContext<ShutdownMessage> context) {
            var assemblyConfigurationAttribute = typeof(ShutdownConsumer).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;

            if(context.Message.ProcessId != Environment.ProcessId && context.Message.Configuration == buildConfigurationName) {
                _logger.LogInformation("Shutting down");
                _applicationLifetime.StopApplication();
            } else {
                _logger.LogInformation("Matching ID so not shutting down");
            }
            return Task.CompletedTask;
        }
    }
    public class ShutdownMessage {
        public ShutdownMessage() {
            var assemblyConfigurationAttribute = typeof(ShutdownMessage).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;
            Configuration = buildConfigurationName;
            ProcessId = Environment.ProcessId;
        }
        public int ProcessId { get; set; }
        public string Configuration { get; set; }

    }

}
