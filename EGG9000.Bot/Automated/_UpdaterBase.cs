using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

using Microsoft.Extensions.Hosting;

namespace EGG9000.Bot.Automated {
    public abstract class _UpdaterBase : IHostedService {
        private Timer _timer;
        private Timer _watchDogTimer;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1);
        private bool Restarted = false;

        public TimeSpan UpdateInterval;
        private TimeSpan _delayedStart;
        private DateTime? _lastMessageSent;
        public DateTime LastStarted;
        public DateTime LastCompleted;

        public DiscordSocketClient _client;

        protected Bugsnag.IClient _bugsnag;

        public _UpdaterBase(TimeSpan updateInterval, TimeSpan delayedStart, DiscordSocketClient client, Bugsnag.IClient bugsnag) {
            Console.WriteLine($"Initiating {this.GetType().Name}");
            UpdateInterval = updateInterval;
            _delayedStart = delayedStart;
            _client = (DiscordSocketClient)client;
            Instance = this;
            _bugsnag = bugsnag;
        }

        public static _UpdaterBase Instance;
        public static void ResetTimeStatic() {
            Instance?.ResetTimer();
        }

        public void ResetTimer() {
            _timer.Change(TimeSpan.Zero, UpdateInterval);
        }

        private async Task _run(object state) {
            Console.WriteLine($"Running {this.GetType().Name}");
            if(await _semaphoreSlim.WaitAsync(TimeSpan.Zero)) {
                try {
                    LastStarted = DateTime.Now;
                    await Run(state);
                    LastCompleted = DateTime.Now;
                    if(Restarted) {
                        Restarted = false;
                        var dmChannel = await _client.GetUser(248865520756064257).CreateDMChannelAsync();
                        await dmChannel.SendMessageAsync($"{this.GetType().Name} successfully restarted.");

                    }
                } catch(Exception e) {
                    _bugsnag.Notify(e);
                    Console.WriteLine($"Error in {this.GetType().Name}: {e.Message}");
                } finally {
                    Console.WriteLine($"Releasing Semaphore for {this.GetType().Name}");
                    _semaphoreSlim.Release();
                }
            } else {
                Console.WriteLine($"Unable to run {this.GetType().Name} because it is alreadsy running");
            }
        }

        public abstract Task Run(object state);

        public Task StartAsync(CancellationToken cancellationToken) {
            _timer = new Timer(async (state) => await _run(state), null, _delayedStart, UpdateInterval);
            _watchDogTimer = new Timer(async (state) => await _WatchDog(state), null, UpdateInterval * 2, UpdateInterval * 2);
            return Task.CompletedTask;

        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            await _timer.DisposeAsync();
            await _watchDogTimer.DisposeAsync();
            if(_semaphoreSlim.CurrentCount > 0) {
                Console.WriteLine($"Waiting on {this.GetType().Name} to shutdown");
            }
            await _semaphoreSlim.WaitAsync(cancellationToken);
        }

        private async Task _WatchDog(object state) {
            if(LastStarted < DateTime.Now - UpdateInterval * 4) {
                if( _lastMessageSent == null || (DateTime.Now - _lastMessageSent).Value.TotalHours > 1) {
                    var dmChannel = await _client.GetUser(248865520756064257).CreateDMChannelAsync();
                    await dmChannel.SendMessageAsync($"Watchdog for {this.GetType().Name}, last started {LastStarted.ToShortTimeString()}, last completed {LastCompleted.ToShortTimeString()}. Attempting Restart.");
                    _semaphoreSlim.Release();
                    Restarted = true;
                    _timer.Change(TimeSpan.Zero, UpdateInterval);
                    _lastMessageSent = DateTime.Now;    
                }
            }
        }
    }
}
