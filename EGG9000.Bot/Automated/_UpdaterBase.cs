using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EGG9000.Bot.Automated {
    public class UpdaterOptions<T> {
        public TimeSpan? DelayStart { get; set; }
    }
    public abstract class _UpdaterBase<T> : IHostedService where T : _UpdaterBase<T> {
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

        protected Bugsnag.IClient _bugsnag;

        protected ulong _CPGuildId;

        public _UpdaterBase(TimeSpan updateInterval, TimeSpan delayedStart, IServiceProvider provider) {
            Console.WriteLine($"Initiating {this.GetType().Name}");
            var options = provider.GetService<IOptionsMonitor<UpdaterOptions<T>>>();
            _configuration = provider.GetService<IConfiguration>();
            UpdateInterval = updateInterval;
            _delayedStart = options.CurrentValue.DelayStart ?? delayedStart;
            _client = provider.GetService<DiscordHostedService>();
            Instance = this;
            _bugsnag = provider.GetService<Bugsnag.IClient>();
            ulong.TryParse(_configuration.GetConnectionString("CPGuildId"), out _CPGuildId);


        }

        public static _UpdaterBase<T> Instance;
        public static void ResetTimeStatic() {
            Instance?.ResetTimer();
        }

        public void ResetTimer() {
            _timer.Change(TimeSpan.Zero, UpdateInterval);
        }

        private async void _run(object state) {
            Console.WriteLine($"Running {this.GetType().Name}");
            if(await _semaphoreSlim.WaitAsync(TimeSpan.Zero)) {
                try {
                    LastStarted = DateTime.Now;
                    await Run(state, _cts.Token);
                    LastCompleted = DateTime.Now;
                    if(Restarted) {
                        Restarted = false;
                        var dmChannel = await _client.GetUser(248865520756064257).CreateDMChannelAsync(new RequestOptions { CancelToken = _cts.Token });
                        await dmChannel.SendMessageAsync($"{this.GetType().Name} successfully restarted.", options: new RequestOptions { CancelToken = _cts.Token});

                    }
                } catch(Exception e) {
                    _bugsnag.Notify(e);
                    Console.WriteLine($"Error in {this.GetType().Name}: {e.Message}");
                } finally {
                    Console.WriteLine($"Releasing Semaphore for {this.GetType().Name}");
                    _semaphoreSlim.Release();
                }
            } else {
                Console.WriteLine($"Unable to run {this.GetType().Name} because it is already running");
            }
        }

        public abstract Task Run(object state, CancellationToken cancellationToken);

        public Task StartAsync(CancellationToken cancellationToken) {
            _timer = new Timer(_run, null, _delayedStart, UpdateInterval);
            _watchDogTimer = new Timer(async (state) => await _WatchDog(state), null, UpdateInterval * 2, UpdateInterval * 2);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            Console.WriteLine($"Stop called for {this.GetType().Name}");
            _cts.Cancel();
            Console.WriteLine("Token Canceled");
            _cts.Dispose();
            Console.WriteLine("CTS Disposed");
            await _timer.DisposeAsync();
            await _watchDogTimer.DisposeAsync();
            if(_semaphoreSlim.CurrentCount > 0) {
                Console.WriteLine($"Waiting on {this.GetType().Name} to shutdown");
            }
            await _semaphoreSlim.WaitAsync(cancellationToken);
            Console.WriteLine($"{this.GetType().Name} has successfully stopped.");
        }

        private async Task _WatchDog(object state) {
            if(LastStarted < DateTime.Now - UpdateInterval * 4) {
                //Console.WriteLine($"Watchdog run for {GetType().Name}");
                if( _lastMessageSent == null || (DateTime.Now - _lastMessageSent).Value.TotalHours > 1) {
                    var success = await AttemptCancel();
                    if(success) {
                        Console.WriteLine($"Successfully canceled task for {GetType().Name}");
                        return;
                    }
                    var dmChannel = await(await _client.GetUserAsync(248865520756064257)).CreateDMChannelAsync(options: new RequestOptions { CancelToken = _cts.Token });
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
