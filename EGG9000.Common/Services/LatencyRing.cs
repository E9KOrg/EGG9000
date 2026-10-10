using System;
using System.Threading;

namespace EGG9000.Common.Services {
    public sealed class LatencyRing {
        public const int Capacity = 512;

        private readonly long[] _slots = new long[Capacity];
        private long _index = -1;

        public void Add(long value) {
            var i = Interlocked.Increment(ref _index);
            _slots[(int)((ulong)i % Capacity)] = value;
        }

        public int Count => (int)Math.Min(Math.Max(Interlocked.Read(ref _index) + 1, 0), Capacity);

        public (long P50, long P95, long P99, long Max) Percentiles() {
            var n = Count;
            if(n <= 0) return (0, 0, 0, 0);
            var copy = new long[n];
            Array.Copy(_slots, copy, n);
            Array.Sort(copy);
            return (Quantile(copy, 0.50), Quantile(copy, 0.95), Quantile(copy, 0.99), copy[n - 1]);
        }

        private static long Quantile(long[] sorted, double q) {
            var idx = (int)Math.Ceiling(q * sorted.Length) - 1;
            return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
        }
    }
}
