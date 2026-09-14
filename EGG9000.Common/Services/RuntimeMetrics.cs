using System;
using System.Threading;

namespace EGG9000.Common.Services {
    /// <summary>
    /// Process wide, lock free runtime counters for cheap in-bot reporting (see the
    /// <c>/a dbload</c> staff command). Counts are cumulative since process start.
    /// Egg Inc API calls are incremented in <c>ContractsAPI</c>; DB queries via the
    /// EF <see cref="QueryCountingInterceptor"/>; commands in the command dispatcher.
    /// </summary>
    public static class RuntimeMetrics {
        public static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

        private static long _dbQueries;
        private static long _apiCalls;
        private static long _apiFailures;
        private static long _commands;
        private static long _commandFailures;
        private static long _discordOps;
        private static long _dbFailures;
        private static long _dbRetries;
        private static long _dbReads;
        private static long _dbWrites;
        private static long _dbSlow;
        private static long _dbLatencyTicks;

        private static readonly LatencyRing _dbLatency = new();

        public static long DbQueries => Interlocked.Read(ref _dbQueries);
        public static long ApiCalls => Interlocked.Read(ref _apiCalls);
        public static long ApiFailures => Interlocked.Read(ref _apiFailures);
        public static long Commands => Interlocked.Read(ref _commands);
        public static long CommandFailures => Interlocked.Read(ref _commandFailures);
        /// <summary>Queued Discord write operations processed (the background-write choke point).</summary>
        public static long DiscordOps => Interlocked.Read(ref _discordOps);

        public static void AddDbQueries(long n = 1) => Interlocked.Add(ref _dbQueries, n);
        public static void AddApiCalls(long n = 1) => Interlocked.Add(ref _apiCalls, n);
        public static void AddApiFailures(long n = 1) => Interlocked.Add(ref _apiFailures, n);
        public static void AddCommands(long n = 1) => Interlocked.Add(ref _commands, n);
        public static void AddCommandFailures(long n = 1) => Interlocked.Add(ref _commandFailures, n);
        public static void AddDiscordOps(long n = 1) => Interlocked.Add(ref _discordOps, n);

        public static long DbFailures => Interlocked.Read(ref _dbFailures);
        public static long DbRetries => Interlocked.Read(ref _dbRetries);
        public static long DbReads => Interlocked.Read(ref _dbReads);
        public static long DbWrites => Interlocked.Read(ref _dbWrites);
        public static long DbSlow => Interlocked.Read(ref _dbSlow);
        public static long DbLatencyTicks => Interlocked.Read(ref _dbLatencyTicks);

        public static double DbMeanMs {
            get {
                var n = DbQueries;
                return n <= 0 ? 0 : DbLatencyTicks / (double)n / TimeSpan.TicksPerMillisecond;
            }
        }

        public static void AddDbFailures(long n = 1) => Interlocked.Add(ref _dbFailures, n);
        public static void AddDbRetries(long n = 1) => Interlocked.Add(ref _dbRetries, n);

        public static void RecordDbCommand(TimeSpan duration, bool write) {
            if(write) Interlocked.Increment(ref _dbWrites); else Interlocked.Increment(ref _dbReads);
            Interlocked.Add(ref _dbLatencyTicks, duration.Ticks);
            if(duration.TotalMilliseconds > DbSlowThresholdMs) Interlocked.Increment(ref _dbSlow);
            _dbLatency.Add(duration.Ticks);
        }

        public const double DbSlowThresholdMs = 250;

        public static (double P50, double P95, double P99, double Max) DbLatencyPercentiles() {
            var (p50, p95, p99, max) = _dbLatency.Percentiles();
            static double Ms(long ticks) => ticks / (double)TimeSpan.TicksPerMillisecond;
            return (Ms(p50), Ms(p95), Ms(p99), Ms(max));
        }

        public static TimeSpan Uptime => DateTimeOffset.UtcNow - StartedAt;

        /// <summary>Average events per minute since process start (0 if no uptime yet).</summary>
        public static double PerMinute(long count) {
            var mins = Uptime.TotalMinutes;
            return mins <= 0 ? 0 : count / mins;
        }
    }
}
