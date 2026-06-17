using EGG9000.Common.Helpers;

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
            if(!SecretsHelper.IsValidBusSecret(context.Message.Secret)) {
                _logger.LogWarning("Rejected RestartMessage with invalid control secret (request ID {id})", context.RequestId);
                return Task.CompletedTask;
            }
            _logger.LogInformation("Restart Consumer called with request ID {id}", context.RequestId);
            Environment.ExitCode = 1;
            _applicationLifetime.StopApplication();

            return Task.CompletedTask;
        }
    }

    public class RestartMessage {
        public string Secret { get; set; } = SecretsHelper.BusControlSecret;
    }
}
