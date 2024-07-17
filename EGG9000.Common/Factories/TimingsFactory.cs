using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EGG9000.Common.Factories {
    public class TimingsFactory(ILogger logger) {
        private readonly ILogger _logger = logger;
        private readonly Stopwatch stopwatch = new();
        private List<(string name, TimeSpan time)> times;

        public TimingsFactory Start() {
            stopwatch.Start();
            times = [];
            return this;
        }

        public void Set(int num) {
            Set(num.ToString());
        }
        public void Set(double num) {
            Set(num.ToString());
        }
        public void Set(string name) {
            times.Add((name, stopwatch.Elapsed));
            _logger?.LogTrace("Timing: {name} {time}", name, stopwatch.Elapsed.Humanize().ShortenTime());
            stopwatch.Restart();
        }

        public List<(string name, TimeSpan time)> Finished() {
            Set("Last");
            var total = TimeSpan.FromTicks(times.Sum(x => x.time.Ticks));
            times.Add(("TOTAL", total));
            _logger?.LogTrace("Timing: {name} {time}", "Total", total.Humanize().ShortenTime());
            stopwatch.Stop();
            return times;
        }
    }
}
