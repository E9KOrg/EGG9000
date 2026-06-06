using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Prometheus;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    internal sealed class PrometheusMetricServerHostedService(IMetricServer metricServer, ILogger<PrometheusMetricServerHostedService> logger) : IHostedService {
        private readonly IMetricServer _metricServer = metricServer;
        private readonly ILogger<PrometheusMetricServerHostedService> _logger = logger;

        public Task StartAsync(CancellationToken cancellationToken) {
            try {
                _metricServer.Start();
                _logger.LogInformation("Prometheus Metric Server started successfully.");
            } catch(Exception e) {
                _logger.LogError(e, "Failed to initialize Prometheus Metric Server.");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _metricServer.Stop();
            return Task.CompletedTask;
        }
    }
}