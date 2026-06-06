using Cronos;

using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.Threading;
using System.Threading.Tasks;


namespace EGG9000.Bot.Automated {
    public class UpdaterOptions<T> {
        public TimeSpan? DelayStart { get; set; }
    }

    public interface IUpdaterService : IHostedService {
        public bool Active();
        public bool Running();
        public void ResetTimer();
    }

    public abstract class _UpdaterBase<T> : IUpdaterService where T : _UpdaterBase<T> {
        private bool initialStart;
        private Timer _timer;
        private Timer _watchDogTimer;
        private DateTimeOffset _lastAlive;
        private readonly SemaphoreSlim _semaphoreSlim = new(1);
        private CancellationTokenSource _cts = new();
        private bool Restarted = false;

        private readonly CronExpression _cronExpression;
        private DateTimeOffset _nextRunFromCron;
        /*private readonly DateTimeOffset _firstRunDue;
        private DateTimeOffset _updaterInitiated;*/

        public TimeSpan UpdateInterval;
        private readonly TimeSpan _delayedStart;
        private DateTime? _lastMessageSent;
        public DateTime LastStarted;
        public DateTime LastCompleted;

        public DiscordHostedService _client;
        public IConfiguration _configuration;
        public IServiceProvider _provider;
        private UpdaterOptions<T> _options;

        protected Bugsnag.IClient _bugSnag;
        protected ILogger<T> _logger;
        protected IDiscordQueue _queue;
        protected IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        protected ulong _CPGuildId;
        protected CoopsBeingCreatedService _coopsBeingCreatedService => _provider.GetService<CoopsBeingCreatedService>();   

        public _UpdaterBase(TimeSpan updateInterval, TimeSpan delayedStart, IServiceProvider provider) {
            _initiate(provider);
            UpdateInterval = updateInterval;
            _delayedStart = _options.DelayStart ?? delayedStart;

        }

        // Ignore Spelling: cron
        public _UpdaterBase(CronExpression cronExpression, IServiceProvider provider) {
            _initiate(provider);
            _cronExpression = cronExpression;
            _nextRunFromCron = _options.DelayStart.HasValue ?
                DateTimeOffset.Now + _options.DelayStart.Value :
                cronExpression.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")).Value;
        }

        private void _initiate(IServiceProvider provider) {
            _logger = provider.GetService<ILogger<T>>();
            _logger.LogInformation("Initiating");
            _configuration = provider.GetService<IConfiguration>();
            _client = provider.GetService<DiscordHostedService>();
            _dbContextFactory = provider.GetService<IDbContextFactory<ApplicationDbContext>>();
            Instance = this;
            _bugSnag = provider.GetService<Bugsnag.IClient>();
            _queue = provider.GetRequiredService<IDiscordQueue>();
            _provider = provider;
            _ = ulong.TryParse(_configuration.GetConnectionString("CPGuildId"), out _CPGuildId);

            initialStart = true;
            _lastAlive = DateTimeOffset.Now;
            _options = provider.GetService<IOptionsMonitor<UpdaterOptions<T>>>().CurrentValue;

        }

        private static _UpdaterBase<T> Instance;
        public static void ResetTimeStatic() {
            Instance?.ResetTimer();
        }

        public void ResetTimer() {
            _timer.Change(TimeSpan.Zero, UpdateInterval);
        }

        public void StillAlive() {
            _lastAlive = DateTimeOffset.Now;
        }

        public async Task WaitOnCoopsBeingCreated(CancellationToken cancellationToken) {
            while(_coopsBeingCreatedService.AreCoopsBeingCreated()) {
                _logger.LogTrace("Waiting on coops being created...");
                await Task.Delay(60000, cancellationToken);
            }
        }

        public void ChangeUpdateInterval(TimeSpan newUpdateInterval) {
            UpdateInterval = newUpdateInterval;
            _logger.LogInformation("Updating interval to {interval}", UpdateInterval);
            if(_timer is null) {
                _timer = new Timer(_runTimer, null, UpdateInterval, UpdateInterval);
            } else {
                _timer.Change(UpdateInterval, UpdateInterval);
            }
        }

        private async void _runTimer(object state) {
            await _run(state);
        }

        private async Task _run(object state) {
            if(await _semaphoreSlim.WaitAsync(TimeSpan.Zero)) {
                try {
                    var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    _logger.LogInformation("Running");
                    LastStarted = DateTime.Now;
                    _lastAlive = DateTimeOffset.Now;
                    var log = new AutomationLog { Type = GetType().Name, StartTime = DateTimeOffset.Now };
                    _db.AutomationLogs.Add(log);
                    await _db.SaveChangesAsync();
                    await Run(state, _cts.Token);
                    log.EndTime = DateTimeOffset.Now;
                    await _db.SaveChangesAsync();

                    LastCompleted = DateTime.Now;
                    if(Restarted) {
                        Restarted = false;
                        await _client.SendDMToKendrome($"{GetType().Name} successfully restarted.");
                    }
                } catch(OperationCanceledException) {
                    _logger.LogInformation("Run cancelled");
                } catch(Exception e) {
                    _bugSnag.Notify(e);
                    _logger.LogError(e, "Error during run: {Message}", e.Message);
                } finally {
                    _logger.LogInformation("Releasing semaphore");
                    _semaphoreSlim.Release();
                }
            } else {
                var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
                _db.AutomationLogs.Add(new AutomationLog { Type = GetType().Name, StartTime = DateTimeOffset.Now, Skipped = true });
                await _db.SaveChangesAsync();
                _logger.LogWarning("Unable to run, already running for {time}", (DateTime.Now - LastStarted).Humanize());
            }
        }

        public abstract Task Run(object state, CancellationToken cancellationToken);

        public bool Active() {
            return _timer is not null;
        }
        public bool Running() {
            return _semaphoreSlim.CurrentCount == 0;
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            try {
                _cts ??= new CancellationTokenSource();

                if(_cronExpression is not null) {
                    _ = LoopForCronExpression();
                    _watchDogTimer = new Timer(async (state) => await _WatchDog(state), this, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
                } else {
                    _timer = new Timer(_runTimer, null, initialStart ? _delayedStart : TimeSpan.Zero, UpdateInterval);
                    _watchDogTimer = new Timer(async (state) => await _WatchDog(state), this, UpdateInterval * 2, UpdateInterval * 2);
                }
                initialStart = false;
            } catch(Exception e) {
                _bugSnag.Notify(e);
                _logger.LogError(e, "Error starting");
            }
            return Task.CompletedTask;
        }

        private async Task LoopForCronExpression() {
            while(_cts?.IsCancellationRequested == false) {
                try {

                    if(_nextRunFromCron < DateTimeOffset.Now) {
                        _logger.LogInformation("Running, Current time: {currentTime}, next run at {nextRun}", DateTimeOffset.Now.ToString("h:mm:ss:ff"), _nextRunFromCron);

                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        await _run(null);
                        _logger.LogInformation("Update took {updateTime}", timer.Elapsed.Humanize());
                        _lastAlive = DateTimeOffset.Now;
                    }


                    _nextRunFromCron = _cronExpression.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")).Value;
                    var delay = (_nextRunFromCron - DateTimeOffset.Now) + TimeSpan.FromMilliseconds(10);
                    if(delay < TimeSpan.Zero) {
                        delay = TimeSpan.FromSeconds(1);
                    }
                    _logger.LogInformation("Next run in {nextRunDelay} at {nextRunTime}", delay.Humanize(), _nextRunFromCron.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss"));
                    await Task.Delay((int)delay.TotalMilliseconds, _cts.Token);
                } catch(TaskCanceledException) {
                    _logger.LogInformation("Cron Loop cancelled");
                } catch(Exception e) {
                    _bugSnag.Notify(e);
                    _logger.LogError(e, "Error running updater");
                }
            }
        }


        public async Task StopAsync(CancellationToken cancellationToken) {
            try {
                _logger.LogInformation("STOP: Called");

                if(_cts is not null) {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }

                if(_cronExpression is not null) {

                } else {
                    if(_timer is not null)
                        await _timer.DisposeAsync();
                    _timer = null;
                }
                await _watchDogTimer.DisposeAsync();

                while(!await _semaphoreSlim.WaitAsync(5000, cancellationToken)) {
                    _logger.LogWarning("STOP: Waiting on semaphore");
                }
                _semaphoreSlim.Release();
                _logger.LogInformation("STOP: Stopped successfully");
            } catch(TaskCanceledException) {
            } catch(Exception e) {
                _bugSnag.Notify(e);
                _logger.LogError(e, "Error stopping");
            }
        }

#pragma warning disable CS1998
        private static async Task _WatchDog(object state) {
            var _this = state as _UpdaterBase<T>;


            DateTimeOffset watchDogDue;

            if(_this._cronExpression is null) {
                watchDogDue = _this._lastAlive + _this.UpdateInterval * 2;
            } else {
                watchDogDue = _this._cronExpression.GetNextOccurrence(_this._lastAlive, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")).Value;
                watchDogDue = _this._cronExpression.GetNextOccurrence(watchDogDue, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")).Value;
            }

            if(watchDogDue < _this._lastAlive.AddMinutes(5))
                watchDogDue = _this._lastAlive + TimeSpan.FromMinutes(5);

            if(watchDogDue < DateTimeOffset.Now) {
                var lastAlive = DateTimeOffset.Now - _this._lastAlive;
                _this._logger.LogWarning("Watchdog Ran, last start {time}, last alive {lastalive}", (DateTime.Now - _this.LastStarted).Humanize(), _this._lastAlive.Humanize());
                if(lastAlive > TimeSpan.FromMinutes(5) && (_this._lastMessageSent == null || (DateTime.Now - _this._lastMessageSent).Value.TotalHours > 1)) {
                    await _this._client.SendDMToKendrome($"Watchdog for {_this.GetType().Name}, last started {_this.LastStarted:t}, last completed {_this.LastCompleted:t}, last alive {_this._lastAlive.DateTime:t}.");
                    _this._lastMessageSent = DateTime.Now;
                }
            }


        }
#pragma warning restore CS1998

    }
}
