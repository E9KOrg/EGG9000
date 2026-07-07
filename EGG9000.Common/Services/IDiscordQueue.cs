using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public interface IDiscordQueue {
        int HighDepth { get; }
        int LowDepth { get; }
        int HighWorkers { get; }
        int LowWorkers { get; }

        /// <summary>tag overrides the auto-captured caller name; useful when enqueuing from a shared helper called from many places.</summary>
        void EnqueueHigh(Func<Task> operation, string tag = null,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
        void EnqueueLow(Func<Task> operation, string tag = null,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
        Task<T> EnqueueHighAsync<T>(Func<Task<T>> operation, CancellationToken ct = default, string tag = null,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
        Task<T> EnqueueLowAsync<T>(Func<Task<T>> operation, CancellationToken ct = default, string tag = null,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
    }
}
