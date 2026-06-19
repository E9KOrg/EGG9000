using EGG9000.Common.Consumers;

using Prometheus;

namespace EGG9000.Site.Services {
    /// <summary>
    /// Owns the bot_* Prometheus gauges and sets them from the latest
    /// <see cref="BotMetricsSnapshotMessage"/> received over the bus. The gauges register into the
    /// default registry, so they surface on the site's auth-gated /metrics alongside the site's own
    /// dotnet_*/process_*/http_* metrics - the cross-scope view. Cumulative bot counters are exported
    /// as gauges holding their running total (rate() still works); staleness is derivable from
    /// bot_last_snapshot_timestamp_seconds. Singleton.
    /// </summary>
    public sealed class BotMetricsExporter {
        private static readonly Gauge LastSnapshot = Metrics.CreateGauge("bot_last_snapshot_timestamp_seconds", "Unix time the most recent bot snapshot was produced (for staleness detection).");
        private static readonly Gauge Uptime = Metrics.CreateGauge("bot_uptime_seconds", "Bot process uptime.");
        private static readonly Gauge WorkingSet = Metrics.CreateGauge("bot_working_set_bytes", "Bot process working set.");
        private static readonly Gauge GcHeap = Metrics.CreateGauge("bot_gc_heap_bytes", "Bot managed GC heap size.");
        private static readonly Gauge GcCollections = Metrics.CreateGauge("bot_gc_collections", "Bot GC collection count by generation.", "generation");
        private static readonly Gauge Threads = Metrics.CreateGauge("bot_threads", "Bot process thread count.");
        private static readonly Gauge CpuSeconds = Metrics.CreateGauge("bot_cpu_seconds", "Bot total CPU seconds consumed.");
        private static readonly Gauge GatewayLatency = Metrics.CreateGauge("bot_gateway_latency_ms", "Bot Discord gateway latency.");
        private static readonly Gauge Guilds = Metrics.CreateGauge("bot_guilds", "Guilds the bot is a member of.");
        private static readonly Gauge SendQueueDepth = Metrics.CreateGauge("bot_send_queue_depth", "Bot Discord send-queue depth by priority.", "priority");
        private static readonly Gauge SendQueueWorkers = Metrics.CreateGauge("bot_send_queue_workers", "Bot Discord send-queue worker count by priority.", "priority");
        private static readonly Gauge ApiCalls = Metrics.CreateGauge("bot_api_calls", "Bot Egg Inc API calls since start.");
        private static readonly Gauge ApiFailures = Metrics.CreateGauge("bot_api_failures", "Bot Egg Inc API failures since start.");
        private static readonly Gauge DbQueries = Metrics.CreateGauge("bot_db_queries", "Bot DB queries since start.");
        private static readonly Gauge Commands = Metrics.CreateGauge("bot_commands", "Bot commands handled since start.");
        private static readonly Gauge CommandFailures = Metrics.CreateGauge("bot_command_failures", "Bot command failures since start.");
        private static readonly Gauge DiscordOps = Metrics.CreateGauge("bot_discord_ops", "Bot Discord write ops processed since start.");

        public void Update(BotMetricsSnapshotMessage m) {
            LastSnapshot.Set(m.TimestampUnix);
            Uptime.Set(m.UptimeSeconds);
            WorkingSet.Set(m.WorkingSetBytes);
            GcHeap.Set(m.GcHeapBytes);
            GcCollections.WithLabels("0").Set(m.GcGen0);
            GcCollections.WithLabels("1").Set(m.GcGen1);
            GcCollections.WithLabels("2").Set(m.GcGen2);
            Threads.Set(m.Threads);
            CpuSeconds.Set(m.CpuSeconds);
            GatewayLatency.Set(m.GatewayLatencyMs);
            Guilds.Set(m.Guilds);
            SendQueueDepth.WithLabels("high").Set(m.SendQueueHighDepth);
            SendQueueDepth.WithLabels("low").Set(m.SendQueueLowDepth);
            SendQueueWorkers.WithLabels("high").Set(m.SendQueueHighWorkers);
            SendQueueWorkers.WithLabels("low").Set(m.SendQueueLowWorkers);
            ApiCalls.Set(m.ApiCalls);
            ApiFailures.Set(m.ApiFailures);
            DbQueries.Set(m.DbQueries);
            Commands.Set(m.Commands);
            CommandFailures.Set(m.CommandFailures);
            DiscordOps.Set(m.DiscordOps);
        }
    }
}
