using EGG9000.Common.Bus;
using EGG9000.Site.Services;

using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Site.Bus {
    public sealed class BotMetricsSnapshotHandler(BotMetricsExporter exporter) : IBusHandler<BotMetricsSnapshotMessage> {
        public Task HandleAsync(BotMetricsSnapshotMessage message, CancellationToken ct) {
            exporter.Update(message);
            return Task.CompletedTask;
        }
    }
}
