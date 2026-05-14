namespace EGG9000.Common.Services {
    public class DiscordQueueOptions {
        public TierOptions High { get; set; } = new() {
            MinWorkers = 3,
            MaxWorkers = 10,
            ScaleUpThreshold = 50,
            ScaleDownThreshold = 5,
            BatchPauseMs = 0,
            ScaleCheckIntervalMs = 5000
        };
        public TierOptions Low { get; set; } = new() {
            MinWorkers = 2,
            MaxWorkers = 20,
            ScaleUpThreshold = 500,
            ScaleDownThreshold = 50,
            BatchPauseMs = 100,
            ScaleCheckIntervalMs = 5000
        };

        public class TierOptions {
            public int MinWorkers { get; set; }
            public int MaxWorkers { get; set; }
            public int ScaleUpThreshold { get; set; }
            public int ScaleDownThreshold { get; set; }
            public int BatchPauseMs { get; set; }
            public int ScaleCheckIntervalMs { get; set; }
        }
    }
}
