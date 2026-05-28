using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class InteractionRoutingService : IHostedService {
        private readonly DiscordHostedService _discord;
        private readonly InteractionService _interactions;
        private readonly IServiceProvider _provider;
        private readonly ILogger<InteractionRoutingService> _logger;
        private readonly Bugsnag.IClient _bugsnag;
        private readonly SemaphoreSlim _semaphore = new(50);

        private static readonly Histogram Duration = Metrics.CreateHistogram("bot_interaction_duration_seconds", "Interaction execution time", new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.005, 2, 12) });
        private static readonly Counter Total = Metrics.CreateCounter("bot_interaction_total", "Total interactions executed by InteractionService");
        private static readonly Counter Failures = Metrics.CreateCounter("bot_interaction_failures_total", "Failed interactions");

        public InteractionRoutingService(DiscordHostedService discord, InteractionService interactions, IServiceProvider provider, ILogger<InteractionRoutingService> logger, Bugsnag.IClient bugsnag) {
            _discord = discord;
            _interactions = interactions;
            _provider = provider;
            _logger = logger;
            _bugsnag = bugsnag;
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            _discord.Gateway.InteractionCreated += OnInteractionCreated;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _discord.Gateway.InteractionCreated -= OnInteractionCreated;
            return Task.CompletedTask;
        }

        private Task OnInteractionCreated(Discord.IDiscordInteraction interaction) {
            var ctx = new SocketInteractionContext(_discord.Gateway, (SocketInteraction)interaction);
            _ = Task.Run(async () => {
                var acquired = await _semaphore.WaitAsync(TimeSpan.FromSeconds(2.5));
                if(!acquired) { _logger.LogWarning("Interaction semaphore limit hit"); return; }
                Total.Inc();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try {
                    await _interactions.ExecuteCommandAsync(ctx, _provider);
                } catch(Exception e) {
                    Failures.Inc();
                    _bugsnag.Notify(e);
                } finally {
                    _semaphore.Release();
                    sw.Stop();
                    Duration.Observe(sw.Elapsed.TotalSeconds);
                }
            });
            return Task.CompletedTask;
        }
    }
}
