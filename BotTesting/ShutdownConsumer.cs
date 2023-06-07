using Discord.Commands;

using MassTransit;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBot {
    public class ShutdownConsumer : IConsumer<ShutdownMessage> {
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly ILogger<ShutdownConsumer> _logger;
        public ShutdownConsumer(IHostApplicationLifetime applicationLifetime, ILogger<ShutdownConsumer> logger) {
            _applicationLifetime = applicationLifetime;
            _logger = logger;
        }
        public Task Consume(ConsumeContext<ShutdownMessage> context) {
            if(context.Message.ProcessId != System.Diagnostics.Process.GetCurrentProcess().Id) {
                _logger.LogInformation("Shutting down");
                _applicationLifetime.StopApplication();
            } else {
                _logger.LogInformation("Matching ID so not shutting down");
            }
            return Task.CompletedTask;
        }
    }
    public class ShutdownMessage {
        public int ProcessId { get; set; }
    }

}
