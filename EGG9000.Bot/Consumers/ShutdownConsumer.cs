using Discord.Commands;

using MassTransit;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Consumers {
    public class ShutdownConsumer : IConsumer<ShutdownMessage> {
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ILogger<ShutdownConsumer> _logger;
        public ShutdownConsumer(IHostApplicationLifetime applicationLifetime, ILogger<ShutdownConsumer> logger) {
            _applicationLifetime = applicationLifetime;
            _logger = logger;
        }
        public Task Consume(ConsumeContext<ShutdownMessage> context) {
            var assemblyConfigurationAttribute = typeof(ShutdownConsumer).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>();
            var buildConfigurationName = assemblyConfigurationAttribute?.Configuration;

            if(context.Message.ProcessId != System.Diagnostics.Process.GetCurrentProcess().Id && context.Message.Configuration == buildConfigurationName) {
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
            ProcessId = Process.GetCurrentProcess().Id;
        }
        public int ProcessId { get; set; }
        public string Configuration { get; set; }

    }

}
