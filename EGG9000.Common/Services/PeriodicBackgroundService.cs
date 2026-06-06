using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public abstract class PeriodicBackgroundService(TimeSpan interval, TimeSpan initialDelay, ILogger logger) : BackgroundService {
        private readonly TimeSpan _interval = interval;
        private readonly TimeSpan _initialDelay = initialDelay;
        protected readonly ILogger _logger = logger;

        protected abstract Task DoWorkAsync(CancellationToken cancellationToken);

        protected async override Task ExecuteAsync(CancellationToken stoppingToken) {
            try {
                if(_initialDelay > TimeSpan.Zero) {
                    await Task.Delay(_initialDelay, stoppingToken);
                }

                using var timer = new PeriodicTimer(_interval);
                while(true) {
                    try {
                        await DoWorkAsync(stoppingToken);
                    } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) {
                        break;
                    } catch(Exception e) {
                        _logger.LogError(e, "{Service} tick failed", GetType().Name);
                    }

                    if(!await timer.WaitForNextTickAsync(stoppingToken)) {
                        break;
                    }
                }
            } catch(OperationCanceledException) { }
        }
    }
}
