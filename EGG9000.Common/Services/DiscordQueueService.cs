using Bugsnag;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class DiscordQueueService(IOptionsMonitor<DiscordQueueOptions> optsMon, ILogger<DiscordQueueService> logger, IClient bugsnag) : IDiscordQueue, IHostedService {
        private record QueueItem(Func<Task> Operation);

        private readonly Channel<QueueItem> _high = Channel.CreateUnbounded<QueueItem>(new() { SingleReader = false });
        private readonly Channel<QueueItem> _low = Channel.CreateUnbounded<QueueItem>(new() { SingleReader = false });

        private readonly IOptionsMonitor<DiscordQueueOptions> _optsMon = optsMon;
        private readonly ILogger<DiscordQueueService> _logger = logger;
        private readonly IClient _bugsnag = bugsnag;

        private readonly List<(Task Task, CancellationTokenSource Cts)> _highWorkers = [];
        private readonly List<(Task Task, CancellationTokenSource Cts)> _lowWorkers = [];
        private readonly SemaphoreSlim _scaleLock = new(1, 1);
        private CancellationTokenSource _serviceCts;

        public int HighDepth => _high.Reader.Count;
        public int LowDepth => _low.Reader.Count;
        public int HighWorkers => _highWorkers.Count;
        public int LowWorkers => _lowWorkers.Count;

        public async Task StartAsync(CancellationToken cancellationToken) {
            _serviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = _serviceCts.Token;
            var opts = _optsMon.CurrentValue;

            await _scaleLock.WaitAsync(cancellationToken);
            try {
                for(var i = 0; i < opts.High.MinWorkers; i++) SpinUpWorker(_high, _highWorkers, opts.High.BatchPauseMs, ct);
                for(var i = 0; i < opts.Low.MinWorkers; i++) SpinUpWorker(_low, _lowWorkers, opts.Low.BatchPauseMs, ct);
            } finally {
                _scaleLock.Release();
            }

            _ = Task.Run(() => ScaleMonitor(_high, _highWorkers, true, ct), ct);
            _ = Task.Run(() => ScaleMonitor(_low, _lowWorkers, false, ct), ct);

            _logger.LogInformation("DiscordQueueService started - HIGH: {h} workers, LOW: {l} workers", opts.High.MinWorkers, opts.Low.MinWorkers);
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _serviceCts?.Cancel();
            _high.Writer.Complete();
            _low.Writer.Complete();
            List<Task> allWorkerTasks;
            await _scaleLock.WaitAsync(cancellationToken);
            try {
                foreach(var w in _highWorkers) { w.Cts.Cancel(); w.Cts.Dispose(); }
                foreach(var w in _lowWorkers) { w.Cts.Cancel(); w.Cts.Dispose(); }
                allWorkerTasks = [.. _highWorkers.Select(w => w.Task), .. _lowWorkers.Select(w => w.Task)];
                _highWorkers.Clear();
                _lowWorkers.Clear();
            } finally {
                _scaleLock.Release();
            }
            await Task.WhenAll(allWorkerTasks).WaitAsync(cancellationToken);
        }

        public void EnqueueHigh(Func<Task> operation) {
            if (!_high.Writer.TryWrite(new QueueItem(operation)))
                _logger.LogWarning("DiscordQueue HIGH dropped item - service is stopped");
        }

        public void EnqueueLow(Func<Task> operation) {
            if (!_low.Writer.TryWrite(new QueueItem(operation)))
                _logger.LogWarning("DiscordQueue LOW dropped item - service is stopped");
        }

        public Task<T> EnqueueHighAsync<T>(Func<Task<T>> operation, CancellationToken ct = default) => EnqueueWithResult(_high, operation, ct);
        public Task<T> EnqueueLowAsync<T>(Func<Task<T>> operation, CancellationToken ct = default) => EnqueueWithResult(_low, operation, ct);

        private Task<T> EnqueueWithResult<T>(Channel<QueueItem> channel, Func<Task<T>> operation, CancellationToken ct) {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reg = ct.Register(static s => ((TaskCompletionSource<T>)s!).TrySetCanceled(), tcs);
            tcs.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
            if (!channel.Writer.TryWrite(new QueueItem(async () => {
                try {
                    tcs.TrySetResult(await operation());
                } catch(Exception ex) {
                    tcs.TrySetException(ex);
                }
            }))) {
                reg.Dispose();
                tcs.TrySetException(new InvalidOperationException("DiscordQueueService is stopped; cannot enqueue."));
            }
            return tcs.Task;
        }

        private void SpinUpWorker(Channel<QueueItem> channel, List<(Task Task, CancellationTokenSource Cts)> workers, int pauseMs, CancellationToken serviceCt) {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(serviceCt);
            var task = Task.Run(() => WorkerLoop(channel, pauseMs, cts.Token), serviceCt);
            workers.Add((task, cts));
        }

        private async Task WorkerLoop(Channel<QueueItem> channel, int pauseMs, CancellationToken ct) {
            try {
                await foreach(var item in channel.Reader.ReadAllAsync(ct)) {
                    try {
                        await item.Operation();
                        RuntimeMetrics.AddDiscordOps();
                    } catch(Discord.Net.HttpException httpEx) when(httpEx.DiscordCode == Discord.DiscordErrorCode.UnknownMessage) {
                        _logger.LogDebug("DiscordQueue: message no longer exists (10008), skipping");
                    } catch(Discord.Net.HttpException httpEx) when(httpEx.DiscordCode == Discord.DiscordErrorCode.MissingPermissions) {
                        var reqType = httpEx.Request?.GetType();
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                        var method = reqType?.GetProperty("Method", flags)?.GetValue(httpEx.Request) as string;
                        var endpoint = reqType?.GetProperty("Endpoint", flags)?.GetValue(httpEx.Request) as string;
                        _logger.LogWarning("DiscordQueue: missing permissions (50013) on {method} {endpoint}", method ?? "?", endpoint ?? "unknown");
                    } catch(TimeoutException) {
                        _logger.LogWarning("DiscordQueue: operation timed out, retrying in 1s");
                        try {
                            await Task.Delay(1000, ct);
                            await item.Operation();
                        } catch(Exception retryEx) {
                            _logger.LogError(retryEx, "DiscordQueue retry also failed");
                            _bugsnag?.Notify(retryEx);
                        }
                    } catch(Exception ex) {
                        _logger.LogError(ex, "DiscordQueue worker error");
                        _bugsnag?.Notify(ex);
                    }
                    if(pauseMs > 0 && !ct.IsCancellationRequested)
                        await Task.Delay(pauseMs, ct);
                }
            } catch(OperationCanceledException) { }
        }

        private async Task ScaleMonitor(Channel<QueueItem> channel, List<(Task Task, CancellationTokenSource Cts)> workers, bool isHigh, CancellationToken ct) {
            while(!ct.IsCancellationRequested) {
                try {
                    var opts = _optsMon.CurrentValue;
                    var tier = isHigh ? opts.High : opts.Low;
                    await Task.Delay(tier.ScaleCheckIntervalMs, ct);

                    var depth = channel.Reader.Count;
                    await _scaleLock.WaitAsync(ct);
                    try {
                        if(depth > tier.ScaleUpThreshold && workers.Count < tier.MaxWorkers) {
                            var serviceCt = _serviceCts.Token;
                            SpinUpWorker(channel, workers, tier.BatchPauseMs, serviceCt);
                            _logger.LogInformation("DiscordQueue {tier} scaled UP to {count} workers (depth={depth})", isHigh ? "HIGH" : "LOW", workers.Count, depth);
                        } else if(depth < tier.ScaleDownThreshold && workers.Count > tier.MinWorkers) {
                            var last = workers[^1];
                            workers.RemoveAt(workers.Count - 1);
                            last.Cts.Cancel();
                            last.Cts.Dispose();
                            _logger.LogInformation("DiscordQueue {tier} scaled DOWN to {count} workers (depth={depth})", isHigh ? "HIGH" : "LOW", workers.Count, depth);
                        }
                    } finally {
                        _scaleLock.Release();
                    }
                } catch(OperationCanceledException) {
                    break;
                } catch(Exception ex) {
                    _logger.LogError(ex, "ScaleMonitor error");
                }
            }
        }
    }
}
