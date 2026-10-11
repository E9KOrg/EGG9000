using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Bus {
    public sealed record RestartMessage;

    public sealed class RestartHandler(IHostApplicationLifetime applicationLifetime, ILogger<RestartHandler> logger) : IBusHandler<RestartMessage> {
        public Task HandleAsync(RestartMessage message, CancellationToken ct) {
            logger.LogInformation("Restart requested over the bus");
            Environment.ExitCode = 1;
            applicationLifetime.StopApplication();
            return Task.CompletedTask;
        }
    }
}
