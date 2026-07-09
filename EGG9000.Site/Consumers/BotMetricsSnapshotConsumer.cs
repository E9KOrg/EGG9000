using EGG9000.Common.Consumers;
using EGG9000.Common.Helpers;
using EGG9000.Site.Services;

using MassTransit;

using Microsoft.Extensions.Logging;

using System.Threading.Tasks;

namespace EGG9000.Site.Consumers {
    /// <summary>
    /// Applies bot runtime snapshots to <see cref="BotMetricsExporter"/> so they surface on the
    /// site's /metrics. Validates the bus control secret like the other control consumers. Bound to a
    /// per-instance temporary queue (broadcast) so every site process gets every snapshot.
    /// </summary>
    public class BotMetricsSnapshotConsumer(BotMetricsExporter exporter, ILogger<BotMetricsSnapshotConsumer> logger) : IConsumer<BotMetricsSnapshotMessage> {
        private readonly BotMetricsExporter _exporter = exporter;
        private readonly ILogger<BotMetricsSnapshotConsumer> _logger = logger;

        public Task Consume(ConsumeContext<BotMetricsSnapshotMessage> context) {
            if(!SecretsHelper.IsValidBusSecret(context.Message.Secret)) {
                _logger.LogWarning("Rejected BotMetricsSnapshotMessage with invalid control secret");
                return Task.CompletedTask;
            }
            _exporter.Update(context.Message);
            return Task.CompletedTask;
        }
    }
}
