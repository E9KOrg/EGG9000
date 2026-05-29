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
        public static readonly DateTimeOffset StartedAt = DateTimeOffset.Now;

        private static long _dbQueries;
        private static long _apiCalls;
        private static long _apiFailures;
        private static long _commands;
        private static long _commandFailures;
        private static long _discordOps;

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

        public static TimeSpan Uptime => DateTimeOffset.Now - StartedAt;

        /// <summary>Average events per minute since process start (0 if no uptime yet).</summary>
        public static double PerMinute(long count) {
            var mins = Uptime.TotalMinutes;
            return mins <= 0 ? 0 : count / mins;
        }
    }
}
