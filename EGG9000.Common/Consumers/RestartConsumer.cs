using MassTransit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace EGG9000.Common.Consumers {
    public class RestartConsumer(IHostApplicationLifetime applicationLifetime, ILogger<RestartConsumer> logger) : IConsumer<RestartMessage> {
        private readonly IHostApplicationLifetime _applicationLifetime = applicationLifetime;
        private readonly ILogger<RestartConsumer> _logger = logger;

        public Task Consume(ConsumeContext<RestartMessage> context) {
            _logger.LogInformation("Restart Consumer called with request ID {id}", context.RequestId);
            Environment.ExitCode = 1;
            _applicationLifetime.StopApplication();

            return Task.CompletedTask;
        }
    }

    public class RestartMessage { }
}
