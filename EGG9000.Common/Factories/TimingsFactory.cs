using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static Ei.ArtifactSpec.Types;

namespace EGG9000.Common.Factories {
    public class TimingsFactory {
        private readonly ILogger _logger;
        private Stopwatch stopwatch;
        private List<(string name, TimeSpan time)> times;
        public TimingsFactory(ILogger logger) {
            stopwatch = new Stopwatch();
            _logger = logger;
        }

        public TimingsFactory Start() {
            stopwatch.Start();
            times = new List<(string name, TimeSpan time)>();
            return this;
        }

        public void Set(int num) {
            Set(num.ToString());
        }
        public void Set(string name) {
            times.Add((name, stopwatch.Elapsed));
            if(_logger is not null)
                _logger.LogTrace("Timing: {name} {time}", name, stopwatch.Elapsed.Humanize().ShortenTime());
            stopwatch.Restart();
        }

        public List<(string name, TimeSpan time)> Finished() {
            Set("Last");
            var total = TimeSpan.FromTicks(times.Sum(x => x.time.Ticks));
            times.Add(("TOTAL", total));
            if(_logger is not null)
                _logger.LogTrace("Timing: {name} {time}", "Total", total.Humanize().ShortenTime());
            stopwatch.Stop();
            return times;
        }
    }
}
