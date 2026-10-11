namespace EGG9000.Common.Bus {
    public sealed class BotMetricsSnapshotMessage {
        public long TimestampUnix { get; set; }
        public double UptimeSeconds { get; set; }

        public long WorkingSetBytes { get; set; }
        public long GcHeapBytes { get; set; }
        public int GcGen0 { get; set; }
        public int GcGen1 { get; set; }
        public int GcGen2 { get; set; }
        public int Threads { get; set; }
        public double CpuSeconds { get; set; }

        public int GatewayLatencyMs { get; set; }
        public int Guilds { get; set; }
        public int SendQueueHighDepth { get; set; }
        public int SendQueueLowDepth { get; set; }
        public int SendQueueHighWorkers { get; set; }
        public int SendQueueLowWorkers { get; set; }

        public long ApiCalls { get; set; }
        public long ApiFailures { get; set; }
        public long DbQueries { get; set; }
        public long Commands { get; set; }
        public long CommandFailures { get; set; }
        public long DiscordOps { get; set; }
    }
}
