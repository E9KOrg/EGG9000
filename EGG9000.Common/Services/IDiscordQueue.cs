using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public interface IDiscordQueue {
        int HighDepth { get; }
        int LowDepth { get; }
        void EnqueueHigh(Func<Task> operation);
        void EnqueueLow(Func<Task> operation);
        Task<T> EnqueueHighAsync<T>(Func<Task<T>> operation, CancellationToken ct = default);
        Task<T> EnqueueLowAsync<T>(Func<Task<T>> operation, CancellationToken ct = default);
    }
}
