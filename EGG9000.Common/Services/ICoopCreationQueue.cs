using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public interface ICoopCreationQueue {
        int Depth { get; }
        int Workers { get; }

        void Enqueue(Func<Task> operation, string tag = null,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
        Task<T> EnqueueAsync<T>(Func<Task<T>> operation, CancellationToken ct = default, string tag = null,
            [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0);
    }
}
