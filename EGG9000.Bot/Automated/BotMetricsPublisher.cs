using Discord.WebSocket;

using EGG9000.Common.Consumers;
using EGG9000.Common.Services;

using MassTransit;

using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    /// <summary>
    /// Publishes a <see cref="BotMetricsSnapshotMessage"/> over the bus on a fixed cadence so the
    /// site can re-expose the bot's runtime as bot_* Prometheus gauges (cross-scope reporting on the
    /// site's auth-gated /metrics). Deliberately a lightweight <see cref="PeriodicBackgroundService"/>
    /// rather than an _UpdaterBase job - the latter writes an AutomationLog row and runs watchdog
    /// machinery on every tick, which is wrong for a cheap 15s heartbeat.
    /// </summary>
    public sealed class BotMetricsPublisher : PeriodicBackgroundService {
        private readonly IPublishEndpoint _publish;
        private readonly DiscordSocketClient _client;
        private readonly IDiscordQueue _queue;

        public BotMetricsPublisher(IPublishEndpoint publish, DiscordSocketClient client, IDiscordQueue queue, ILogger<BotMetricsPublisher> logger)
            : base(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), logger) {
            _publish = publish;
            _client = client;
            _queue = queue;
        }

        protected override async Task DoWorkAsync(CancellationToken cancellationToken) {
            var proc = Process.GetCurrentProcess();
            var msg = new BotMetricsSnapshotMessage {
                TimestampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UptimeSeconds = RuntimeMetrics.Uptime.TotalSeconds,
                WorkingSetBytes = proc.WorkingSet64,
                GcHeapBytes = GC.GetTotalMemory(false),
                GcGen0 = GC.CollectionCount(0),
                GcGen1 = GC.CollectionCount(1),
                GcGen2 = GC.CollectionCount(2),
                Threads = proc.Threads.Count,
                CpuSeconds = proc.TotalProcessorTime.TotalSeconds,
                GatewayLatencyMs = _client.Latency,
                Guilds = _client.Guilds?.Count ?? 0,
                SendQueueHighDepth = _queue.HighDepth,
                SendQueueLowDepth = _queue.LowDepth,
                SendQueueHighWorkers = _queue.HighWorkers,
                SendQueueLowWorkers = _queue.LowWorkers,
                ApiCalls = RuntimeMetrics.ApiCalls,
                ApiFailures = RuntimeMetrics.ApiFailures,
                DbQueries = RuntimeMetrics.DbQueries,
                Commands = RuntimeMetrics.Commands,
                CommandFailures = RuntimeMetrics.CommandFailures,
                DiscordOps = RuntimeMetrics.DiscordOps,
            };
            await _publish.Publish(msg, cancellationToken);
        }
    }
}
