using Bugsnag;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class CoopCreationQueueService(IOptionsMonitor<CoopCreationQueueOptions> optsMon, ILogger<CoopCreationQueueService> logger, IClient bugsnag) : ICoopCreationQueue, IHostedService {
        private record QueueItem(Func<Task> Operation, string Caller);

        private readonly Channel<QueueItem> _channel = Channel.CreateUnbounded<QueueItem>(new() { SingleReader = false });
        private readonly IOptionsMonitor<CoopCreationQueueOptions> _optsMon = optsMon;
        private readonly ILogger<CoopCreationQueueService> _logger = logger;
        private readonly IClient _bugsnag = bugsnag;

        private readonly List<(Task Task, CancellationTokenSource Cts)> _workers = [];
        private readonly SemaphoreSlim _scaleLock = new(1, 1);
        private CancellationTokenSource _serviceCts;

        public int Depth => _channel.Reader.Count;
        public int Workers => _workers.Count;

        public async Task StartAsync(CancellationToken cancellationToken) {
            _serviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ct = _serviceCts.Token;
            var opts = _optsMon.CurrentValue;

            await _scaleLock.WaitAsync(cancellationToken);
            try {
                for(var i = 0; i < opts.MinWorkers; i++) SpinUpWorker(opts.BatchPauseMs, ct);
            } finally {
                _scaleLock.Release();
            }

            _ = Task.Run(() => ScaleMonitor(ct), ct);

            _logger.LogInformation("CoopCreationQueueService started - {w} workers", opts.MinWorkers);
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _serviceCts?.Cancel();
            _channel.Writer.Complete();
            List<Task> allWorkerTasks;
            await _scaleLock.WaitAsync(cancellationToken);
            try {
                foreach(var w in _workers) { w.Cts.Cancel(); w.Cts.Dispose(); }
                allWorkerTasks = [.. _workers.Select(w => w.Task)];
                _workers.Clear();
            } finally {
                _scaleLock.Release();
            }
            await Task.WhenAll(allWorkerTasks).WaitAsync(cancellationToken);
        }

        public void Enqueue(Func<Task> operation, string tag = null,
                [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) {
            if(!_channel.Writer.TryWrite(new QueueItem(operation, BuildCallerTag(tag, member, file, line))))
                _logger.LogWarning("CoopCreationQueue dropped item - service is stopped");
        }

        public Task<T> EnqueueAsync<T>(Func<Task<T>> operation, CancellationToken ct = default, string tag = null,
                [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) {
            var caller = BuildCallerTag(tag, member, file, line);
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reg = ct.Register(static s => ((TaskCompletionSource<T>)s!).TrySetCanceled(), tcs);
            tcs.Task.ContinueWith(_ => reg.Dispose(), TaskScheduler.Default);
            if(!_channel.Writer.TryWrite(new QueueItem(async () => {
                try {
                    tcs.TrySetResult(await operation());
                } catch(Exception ex) {
                    tcs.TrySetException(ex);
                }
            }, caller))) {
                reg.Dispose();
                tcs.TrySetException(new InvalidOperationException("CoopCreationQueueService is stopped; cannot enqueue."));
            }
            return tcs.Task;
        }

        private static string BuildCallerTag(string tag, string member, string file, int line)
            => tag ?? $"{Path.GetFileNameWithoutExtension(file)}.{member}:{line}";

        private void SpinUpWorker(int pauseMs, CancellationToken serviceCt) {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(serviceCt);
            var task = Task.Run(() => WorkerLoop(pauseMs, cts.Token), serviceCt);
            _workers.Add((task, cts));
        }

        private async Task WorkerLoop(int pauseMs, CancellationToken ct) {
            try {
                await foreach(var item in _channel.Reader.ReadAllAsync(ct)) {
                    try {
                        await item.Operation();
                        RuntimeMetrics.AddDiscordOps();
                    } catch(Discord.Net.HttpException httpEx) when(httpEx.DiscordCode == Discord.DiscordErrorCode.UnknownMessage) {
                        _logger.LogDebug("CoopCreationQueue [{caller}]: message no longer exists (10008), skipping", item.Caller);
                    } catch(Discord.Net.HttpException httpEx) when((int)httpEx.DiscordCode == 50013) {
                        var reqType = httpEx.Request?.GetType();
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                        var method = reqType?.GetProperty("Method", flags)?.GetValue(httpEx.Request) as string;
                        var endpoint = reqType?.GetProperty("Endpoint", flags)?.GetValue(httpEx.Request) as string;
                        _logger.LogWarning("CoopCreationQueue [{caller}]: missing permissions (50013) on {method} {endpoint}", item.Caller, method ?? "?", endpoint ?? "unknown");
                    } catch(TimeoutException) {
                        _logger.LogWarning("CoopCreationQueue [{caller}]: operation timed out, retrying in 1s", item.Caller);
                        try {
                            await Task.Delay(1000, ct);
                            await item.Operation();
                        } catch(Exception retryEx) {
                            _logger.LogError(retryEx, "CoopCreationQueue [{caller}]: retry also failed", item.Caller);
                            _bugsnag?.Notify(retryEx, report => report.Event.Metadata.Add("CoopCreationQueue", new { caller = item.Caller }));
                        }
                    } catch(Exception ex) {
                        _logger.LogError(ex, "CoopCreationQueue [{caller}]: worker error", item.Caller);
                        _bugsnag?.Notify(ex, report => report.Event.Metadata.Add("CoopCreationQueue", new { caller = item.Caller }));
                    }
                    if(pauseMs > 0 && !ct.IsCancellationRequested)
                        await Task.Delay(pauseMs, ct);
                }
            } catch(OperationCanceledException) { }
        }

        private async Task ScaleMonitor(CancellationToken ct) {
            while(!ct.IsCancellationRequested) {
                try {
                    var opts = _optsMon.CurrentValue;
                    await Task.Delay(opts.ScaleCheckIntervalMs, ct);

                    var depth = _channel.Reader.Count;
                    await _scaleLock.WaitAsync(ct);
                    try {
                        if(depth > opts.ScaleUpThreshold && _workers.Count < opts.MaxWorkers) {
                            SpinUpWorker(opts.BatchPauseMs, _serviceCts.Token);
                            _logger.LogInformation("CoopCreationQueue scaled UP to {count} workers (depth={depth})", _workers.Count, depth);
                        } else if(depth < opts.ScaleDownThreshold && _workers.Count > opts.MinWorkers) {
                            var last = _workers[^1];
                            _workers.RemoveAt(_workers.Count - 1);
                            last.Cts.Cancel();
                            // Don't dispose until the worker's current operation actually finishes - most
                            // awaited Discord.Net calls won't observe this token mid-flight, so disposing
                            // right away would ObjectDisposedException the worker on its next loop check.
                            _ = last.Task.ContinueWith(_ => last.Cts.Dispose(), TaskScheduler.Default);
                            _logger.LogInformation("CoopCreationQueue scaled DOWN to {count} workers (depth={depth})", _workers.Count, depth);
                        }
                    } finally {
                        _scaleLock.Release();
                    }
                } catch(OperationCanceledException) {
                    break;
                } catch(Exception ex) {
                    _logger.LogError(ex, "CoopCreationQueue ScaleMonitor error");
                }
            }
        }
    }
}
