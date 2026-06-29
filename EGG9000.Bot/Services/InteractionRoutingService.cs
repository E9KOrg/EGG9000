using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Services {
    public class InteractionRoutingService(DiscordHostedService discord, InteractionService interactions, IServiceProvider provider, ILogger<InteractionRoutingService> logger, Bugsnag.IClient bugsnag) : IHostedService {
        private readonly DiscordHostedService _discord = discord;
        private readonly InteractionService _interactions = interactions;
        private readonly IServiceProvider _provider = provider;
        private readonly ILogger<InteractionRoutingService> _logger = logger;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;
        private readonly SemaphoreSlim _semaphore = new(50);

        private static readonly Histogram Duration = Metrics.CreateHistogram("bot_interaction_duration_seconds", "Interaction execution time", new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.005, 2, 12) });
        private static readonly Counter Total = Metrics.CreateCounter("bot_interaction_total", "Total interactions executed by InteractionService");
        private static readonly Counter Failures = Metrics.CreateCounter("bot_interaction_failures_total", "Failed interactions");

        public async Task StartAsync(CancellationToken cancellationToken) {
            await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _provider);
            _interactions.SlashCommandExecuted += (i, c, r) => OnExecuted(i, c, r);
            _interactions.ComponentCommandExecuted += (i, c, r) => OnExecuted(i, c, r);
            _interactions.ModalCommandExecuted += (i, c, r) => OnExecuted(i, c, r);
            _discord.Gateway.InteractionCreated += OnInteractionCreated;

            foreach(var guild in _discord.Guilds) {
                await _interactions.RegisterCommandsToGuildAsync(guild.Id);
            }
            await _interactions.RegisterCommandsGloballyAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _discord.Gateway.InteractionCreated -= OnInteractionCreated;
            return Task.CompletedTask;
        }

        private Task OnInteractionCreated(IDiscordInteraction interaction) {
            var ctx = new SocketInteractionContext(_discord.Gateway, (SocketInteraction)interaction);
            _ = Task.Run(async () => {
                var acquired = await _semaphore.WaitAsync(TimeSpan.FromSeconds(2.5));
                if(!acquired) {
                    _logger.LogWarning("Interaction semaphore limit hit");
                    Failures.Inc();
                    try {
                        var dropEmbed = EmbedError("The bot is currently overloaded. Please try again in a moment.");
                        if(interaction.HasResponded) await interaction.ModifyOriginalResponseAsync(m => { m.Content = ""; m.Embed = dropEmbed; });
                        else await interaction.RespondAsync(text: "", embed: dropEmbed, ephemeral: true);
                    } catch(Exception) { }
                    return;
                }
                Total.Inc();
                // Skip autocomplete from the command counter to match the legacy CommandService behavior
                // (autocomplete fires many times per typed character; counting it skews per-minute rates).
                if(interaction is SocketAutocompleteInteraction)
                    _logger.LogDebug("Autocomplete for {command} by {username}", CommandName(interaction), interaction.User?.Username);
                else {
                    EGG9000.Common.Services.RuntimeMetrics.AddCommands();
                    _logger.LogInformation("Running command {command} for user: {username}", CommandName(interaction), interaction.User?.Username);
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try {
                    await _interactions.ExecuteCommandAsync(ctx, _provider);
                } catch(Exception e) {
                    Failures.Inc();
                    EGG9000.Common.Services.RuntimeMetrics.AddCommandFailures();
                    _logger.LogError(e, "Interaction {command} threw", CommandName(interaction));
                    _bugsnag.Notify(e);
                } finally {
                    _semaphore.Release();
                    sw.Stop();
                    Duration.Observe(sw.Elapsed.TotalSeconds);
                }
            });
            return Task.CompletedTask;
        }

        private async Task OnExecuted(ICommandInfo info, IInteractionContext ctx, IResult result) {
            if(result.IsSuccess) return;
            Failures.Inc();
            EGG9000.Common.Services.RuntimeMetrics.AddCommandFailures();
            var interaction = ctx.Interaction;
            Embed embed;
            if(result is ExecuteResult er && er.Exception is not null) {
                _logger.LogError(er.Exception, "Command {command} failed for {username}", info?.Name, interaction.User?.Username);
                _bugsnag.Notify(er.Exception);
                embed = EmbedExceptionFrame(er.Exception);
            } else {
                _logger.LogWarning("Command {command} failed for {username}: {error} - {reason}", info?.Name, interaction.User?.Username, result.Error, result.ErrorReason);
                embed = EmbedError(result.ErrorReason ?? "Command could not be completed.");
            }
            try {
                if(interaction.HasResponded) await interaction.ModifyOriginalResponseAsync(m => { m.Content = ""; m.Embed = embed; });
                else await interaction.RespondAsync(text: "", embed: embed, ephemeral: true);
            } catch(Exception) { }
        }

        private static string CommandName(IDiscordInteraction interaction) => interaction switch {
            SocketSlashCommand s when s.Data.Options.Any(o => o.Type == ApplicationCommandOptionType.SubCommand)
                => $"{s.Data.Name} {s.Data.Options.First(o => o.Type == ApplicationCommandOptionType.SubCommand).Name}",
            SocketSlashCommand s => s.Data.Name,
            SocketUserCommand u => u.Data.Name,
            SocketMessageComponent c => c.Data.CustomId,
            SocketModal m => m.Data.CustomId,
            SocketAutocompleteInteraction a => a.Data.CommandName,
            _ => interaction.Type.ToString()
        };
    }
}
