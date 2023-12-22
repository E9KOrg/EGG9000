using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

using EGG9000.Common.Services;
using EGG9000.Common.Database;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Humanizer;
using Cronos;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace EGG9000.Bot.Automated {
    public class UpdaterOptions<T> {
        public TimeSpan? DelayStart { get; set; }
    }

    public interface IUpdaterService : IHostedService {
        public bool Running();
        public void ResetTimer();
    }

    public abstract class _UpdaterBase<T> : IUpdaterService where T : _UpdaterBase<T> {
        private bool initialStart;
        private Timer _timer;
        private Timer _watchDogTimer;
        private DateTimeOffset _lastAlive;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1);
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool Restarted = false;

        private CronExpression _cronExpression;
        private DateTimeOffset _nextRunFromCron;
        private DateTimeOffset _firstRunDue;
        private DateTimeOffset _updaterInitiated;

        public TimeSpan UpdateInterval;
        private TimeSpan _delayedStart;
        private DateTime? _lastMessageSent;
        public DateTime LastStarted;
        public DateTime LastCompleted;

        public DiscordHostedService _client;
        public IConfiguration _configuration;
        public IServiceProvider _provider;
        private UpdaterOptions<T> _options;
        public APILink _apiLink;

        protected Bugsnag.IClient _bugsnag;
        protected ILogger<T> _logger;
        protected IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        protected ulong _CPGuildId;


        public _UpdaterBase(TimeSpan updateInterval, TimeSpan delayedStart, IServiceProvider provider) {
            _initiate(provider);
            UpdateInterval = updateInterval;
            _delayedStart = _options.DelayStart ?? delayedStart;

        }

        public _UpdaterBase(CronExpression cronExpression, IServiceProvider provider) {
            _initiate(provider);
            _cronExpression = cronExpression;
            _nextRunFromCron = _options.DelayStart.HasValue ? 
                DateTimeOffset.Now + _options.DelayStart.Value : 
                cronExpression.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")).Value;
            _firstRunDue = _nextRunFromCron;
        }

        private void _initiate(IServiceProvider provider) {
            _logger = provider.GetService<ILogger<T>>();
            _logger.LogInformation("Initiating");
            _configuration = provider.GetService<IConfiguration>();
            _client = provider.GetService<DiscordHostedService>();
            _apiLink = provider.GetService<APILink>();
            _dbContextFactory = provider.GetService<IDbContextFactory<ApplicationDbContext>>();
            Instance = this;
            _bugsnag = provider.GetService<Bugsnag.IClient>();
            _provider = provider;
            ulong.TryParse(_configuration.GetConnectionString("CPGuildId"), out _CPGuildId);

            initialStart = true;
            _updaterInitiated = DateTimeOffset.Now;
            _options = provider.GetService<IOptionsMonitor<UpdaterOptions<T>>>().CurrentValue;

        }

        public static _UpdaterBase<T> Instance;
        public static void ResetTimeStatic() {
            Instance?.ResetTimer();
        }

        public void ResetTimer() {
            _timer.Change(TimeSpan.Zero, UpdateInterval);
        }

        public void StillAlive() {
            _lastAlive = DateTimeOffset.Now;
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
            _logger.LogInformation("Running");
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if(await _semaphoreSlim.WaitAsync(TimeSpan.Zero)) {
                try {
                    LastStarted = DateTime.Now;
                    _lastAlive = DateTimeOffset.Now;
                    var log = new AutomationLog { Type = this.GetType().Name, StartTime = DateTimeOffset.Now };
                    _db.AutomationLogs.Add(log);
                    await _db.SaveChangesAsync();
                    await Run(state, _cts.Token);
                    log.EndTime = DateTimeOffset.Now;
                    await _db.SaveChangesAsync();

                    LastCompleted = DateTime.Now;
                    if(Restarted) {
                        Restarted = false;
                        var dmChannel = await _client.GetUser(248865520756064257).CreateDMChannelAsync(new RequestOptions { CancelToken = _cts.Token });
                        await dmChannel.SendMessageAsync($"{this.GetType().Name} successfully restarted.", options: new RequestOptions { CancelToken = _cts.Token });

                    }
                } catch(Exception e) {
                    _bugsnag.Notify(e);
                    _logger.LogError(e, "Error during run: {Message}", e.Message);
                } finally {
                    _logger.LogInformation("Releasing semaphore");
                    _semaphoreSlim.Release();
                }
            } else {
                _db.AutomationLogs.Add(new AutomationLog { Type = this.GetType().Name, StartTime = DateTimeOffset.Now, Skipped = true });
                await _db.SaveChangesAsync();
                _logger.LogWarning("Unable to run, already running for {time}", (DateTime.Now - LastStarted).Humanize());
            }
        }

        public abstract Task Run(object state, CancellationToken cancellationToken);

        public bool Running() {
            return _timer is not null;
        }
        public Task StartAsync(CancellationToken cancellationToken) {
            try {
                if(_cts is null)
                    _cts = new CancellationTokenSource();
                initialStart = false;

                if(_cronExpression is not null) {
                    _ = LoopForCronExpression();
                    _watchDogTimer = new Timer(async (state) => await _WatchDog(state), null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
                } else {
                    _timer = new Timer(_runTimer, null, initialStart ? _delayedStart : TimeSpan.Zero, UpdateInterval);
                    _watchDogTimer = new Timer(async (state) => await _WatchDog(state), null, UpdateInterval * 2, UpdateInterval * 2);
                }
            } catch(Exception e) {
                _bugsnag.Notify(e);
                _logger.LogError(e, "Error starting");
            }
            return Task.CompletedTask;
        }

        private async Task LoopForCronExpression() {
            while(!_cts.IsCancellationRequested) {
                try {

                    if(_nextRunFromCron < DateTimeOffset.Now) {
                        _logger.LogInformation($"Running, Current time: {DateTimeOffset.Now.ToString("h:mm:ss:ff")}, next run at {_nextRunFromCron}");

                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        await _run(null);
                        _logger.LogInformation($"Update took {timer.Elapsed.Humanize()}");
                        _lastAlive = DateTimeOffset.Now;
                    }


                    _nextRunFromCron = _cronExpression.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")).Value;
                    var delay = (_nextRunFromCron - DateTimeOffset.Now) + TimeSpan.FromMilliseconds(10);
                    if(delay < TimeSpan.Zero) {
                        delay = TimeSpan.FromSeconds(1);
                    }
                    _logger.LogInformation($"Next run in {delay.Humanize()} at {_nextRunFromCron.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss")}");
                    await Task.Delay((int)delay.TotalMilliseconds, _cts.Token);

                } catch(Exception e) {
                    _bugsnag.Notify(e);
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
                    await _timer.DisposeAsync();
                    _timer = null;
                }
                await _watchDogTimer.DisposeAsync();

                while(!await _semaphoreSlim.WaitAsync(5000)) {
                    _logger.LogWarning("STOP: Waiting on semaphore");
                }
                _semaphoreSlim.Release();
                _logger.LogInformation("STOP: Stopped successfully");
            } catch(Exception e) {
                _bugsnag.Notify(e);
                _logger.LogError(e, "Error stopping");
            }
        }

        private async Task _WatchDog(object state) {
            //if(_cronExpression is not null) {
            //    if(_firstRunDue > DateTimeOffset.Now) {
            //        _logger.LogTrace("Watchdog skipped because first run not due.");
            //        return;
            //    }
            //}

            //if(_lastAlive > DateTimeOffset.Now.AddMinutes(-5)) {
            //    _logger.LogInformation("Watchdog skipped because last alive is less than 5 minutes.");
            //    return;
            //}

            //var watchDogDue = _cronExpression is not null ? DateTime.Now.AddMinutes(30) : _lastAlive + UpdateInterval * 2;

            //if(LastStarted < watchDogDue) {
            //    var lastAlive = DateTimeOffset.Now - _lastAlive;
            //    _logger.LogWarning("Watchdog Ran, last start {time}, last alive {lastalive}", (DateTime.Now - LastStarted).Humanize(), _lastAlive.Humanize());
            //    if(lastAlive > TimeSpan.FromMinutes(5) && (_lastMessageSent == null || (DateTime.Now - _lastMessageSent).Value.TotalHours > 1)) {
            //        var success = await AttemptCancel();
            //        var dmChannel = await (await _client.GetUserAsync(248865520756064257)).CreateDMChannelAsync(options: new RequestOptions { CancelToken = _cts.Token });
            //        if(success) {
            //            await dmChannel.SendMessageAsync($"Watchdog for {this.GetType().Name}, last started {LastStarted.ToShortTimeString()}, last completed {LastCompleted.ToShortTimeString()}. Restart Succeeded.", options: new RequestOptions { CancelToken = _cts.Token });
            //            return;
            //        }
            //        await dmChannel.SendMessageAsync($"Watchdog for {this.GetType().Name}, last started {LastStarted.ToShortTimeString()}, last completed {LastCompleted.ToShortTimeString()}. Attempting Restart.", options: new RequestOptions { CancelToken = _cts.Token });
            //        _semaphoreSlim.Release();
            //        Restarted = true;
            //        _lastMessageSent = DateTime.Now;

            //        if(_cronExpression is not null) {
            //            _ = LoopForCronExpression();
            //        } else {
            //            _timer.Change(TimeSpan.Zero, UpdateInterval);
            //        }

            //    }
            //}
        }

        private async Task<bool> AttemptCancel() {
            _cts.Cancel();
            for(var i = 0; i < 100; i++) {
                if(_semaphoreSlim.CurrentCount == 0) {
                    break;
                }
                await Task.Delay(1000);
            }
            _cts.Dispose();
            _cts = new CancellationTokenSource();
            if(_semaphoreSlim.CurrentCount == 0)
                return true;
            return false;
        }
    }
}
