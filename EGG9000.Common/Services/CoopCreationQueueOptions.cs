namespace EGG9000.Common.Services {
    public class CoopCreationQueueOptions {
        public int MinWorkers { get; set; } = 4;
        public int MaxWorkers { get; set; } = 16;
        public int ScaleUpThreshold { get; set; } = 50;
        public int ScaleDownThreshold { get; set; } = 5;
        public int BatchPauseMs { get; set; } = 0;
        public int ScaleCheckIntervalMs { get; set; } = 5000;
    }
}
