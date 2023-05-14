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

namespace EGG9000.Bot.Automated {
    public class UpdaterOptions<T> {
        public TimeSpan? DelayStart { get; set; }
    }

    public interface IUpdaterService : IHostedService {
        public bool Running();
    }

    public abstract class _UpdaterBase<T> : IUpdaterService where T : _UpdaterBase<T> {
        private bool initialStart;
        private Timer _timer;
        private Timer _watchDogTimer;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1);
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool Restarted = false;

        public TimeSpan UpdateInterval;
        private TimeSpan _delayedStart;
        private DateTime? _lastMessageSent;
        public DateTime LastStarted;
        public DateTime LastCompleted;

        public DiscordHostedService _client;
        public IConfiguration _configuration;
        public IServiceProvider _provider;

        protected Bugsnag.IClient _bugsnag;
        protected ILogger<T> _logger;

        protected ulong _CPGuildId;

        public _UpdaterBase(TimeSpan updateInterval, TimeSpan delayedStart, IServiceProvider provider) {
            _logger = provider.GetService<ILogger<T>>();
            _logger.LogInformation("Initiating");
            var options = provider.GetService<IOptionsMonitor<UpdaterOptions<T>>>();
            _configuration = provider.GetService<IConfiguration>();
            UpdateInterval = updateInterval;
            _delayedStart = options.CurrentValue.DelayStart ?? delayedStart;
            _client = provider.GetService<DiscordHostedService>();
            Instance = this;
            _bugsnag = provider.GetService<Bugsnag.IClient>();
            _provider = provider;
            ulong.TryParse(_configuration.GetConnectionString("CPGuildId"), out _CPGuildId);

            initialStart = true;
        }

        public static _UpdaterBase<T> Instance;
        public static void ResetTimeStatic() {
            Instance?.ResetTimer();
        }

        public void ResetTimer() {
            _timer.Change(TimeSpan.Zero, UpdateInterval);
        }

        public void ChangeUpdateInterval(TimeSpan newUpdateInterval) {
            UpdateInterval = newUpdateInterval;
            _timer.Change(UpdateInterval, UpdateInterval);
        }

        private async void _run(object state) {
            _logger.LogInformation("Running");
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if(await _semaphoreSlim.WaitAsync(TimeSpan.Zero)) {
                try {
                    LastStarted = DateTime.Now;
                    var log = new Common.Database.Entities.AutomationLog { Type = this.GetType().Name, StartTime = DateTimeOffset.Now };
                    _db.AutomationLogs.Add(log);
                    await _db.SaveChangesAsync();
                    await Run(state, _cts.Token);
                    log.EndTime = DateTimeOffset.Now;
                    await _db.SaveChangesAsync();

                    LastCompleted = DateTime.Now;
                    if(Restarted) {
                        Restarted = false;
                        var dmChannel = await _client.GetUser(248865520756064257).CreateDMChannelAsync(new RequestOptions { CancelToken = _cts.Token });
                        await dmChannel.SendMessageAsync($"{this.GetType().Name} successfully restarted.", options: new RequestOptions { CancelToken = _cts.Token});

                    }
                } catch(Exception e) {
                    _bugsnag.Notify(e);
                    _logger.LogError("Error during run: {Message}", e.Message);
                } finally {
                    _logger.LogInformation("Releasing semaphore");
                    _semaphoreSlim.Release();
                }
            } else {
                _db.AutomationLogs.Add(new Common.Database.Entities.AutomationLog { Type = this.GetType().Name, StartTime = DateTimeOffset.Now, Skipped = true });
                await _db.SaveChangesAsync();
                _logger.LogWarning("Unable to run, already running for {time}", (DateTime.Now - LastStarted).Humanize() );
            }
        }

        public abstract Task Run(object state, CancellationToken cancellationToken);

        public bool Running() {
            return _timer is not null;
        }
        public Task StartAsync(CancellationToken cancellationToken) {
            _timer = new Timer(_run, null, initialStart ? _delayedStart : TimeSpan.Zero, UpdateInterval);
            _watchDogTimer = new Timer(async (state) => await _WatchDog(state), null, UpdateInterval * 2, UpdateInterval * 2);
            initialStart = false;
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("STOP: Called");
            _cts.Cancel();
            _logger.LogInformation("STOP: Token Cancelled");
            _cts.Dispose();
            _logger.LogInformation("STOP: CTS Disposed");
            await _timer.DisposeAsync();
            _timer = null;
            await _watchDogTimer.DisposeAsync();
            _logger.LogInformation("STOP: Timers Disposed");

            if(_semaphoreSlim.CurrentCount > 0) {
                _logger.LogWarning("STOP: Waiting to shutdown, semaphore locked");
            }
            await _semaphoreSlim.WaitAsync(cancellationToken);
            _logger.LogWarning("STOP: Stopped successfully");
        }

        private async Task _WatchDog(object state) {
            if(LastStarted < DateTime.Now - UpdateInterval * 4) {
                _logger.LogWarning("Watchdog Ran, last start {time}", (DateTime.Now - LastStarted).Humanize());
                if( _lastMessageSent == null || (DateTime.Now - _lastMessageSent).Value.TotalHours > 1) {
                    var success = await AttemptCancel();
                    var dmChannel = await (await _client.GetUserAsync(248865520756064257)).CreateDMChannelAsync(options: new RequestOptions { CancelToken = _cts.Token });
                    if(success) {
                        await dmChannel.SendMessageAsync($"Watchdog for {this.GetType().Name}, last started {LastStarted.ToShortTimeString()}, last completed {LastCompleted.ToShortTimeString()}. Restart Succeeded.", options: new RequestOptions { CancelToken = _cts.Token });
                        return;
                    }
                    await dmChannel.SendMessageAsync($"Watchdog for {this.GetType().Name}, last started {LastStarted.ToShortTimeString()}, last completed {LastCompleted.ToShortTimeString()}. Attempting Restart.", options: new RequestOptions { CancelToken = _cts.Token });
                    _semaphoreSlim.Release();
                    Restarted = true;
                    _timer.Change(TimeSpan.Zero, UpdateInterval);
                    _lastMessageSent = DateTime.Now;    
                }
            }
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
