using Discord;
using Discord.Interactions;
using Discord.Rest;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord.ComponentsV2;
using EGG9000.Common.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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
            await RemoveModulesDisallowedForCurrentBuildConfigAsync();
            _interactions.SlashCommandExecuted += (i, c, r) => OnExecuted(i, c, r);
            _interactions.ComponentCommandExecuted += (i, c, r) => OnExecuted(i, c, r);
            _interactions.ModalCommandExecuted += (i, c, r) => OnExecuted(i, c, r);
            _discord.Gateway.InteractionCreated += OnInteractionCreated;

            // Global registration only - guild-scoped registration on top of this duplicated every
            // command in every guild (the old CommandService partitioned DM-eligible commands
            // globally vs guild-only commands per-guild, never both; global-only is the simplest
            // correct replacement under Discord.NET's InteractionService, which doesn't support
            // that partition without breaking [Group] attribution across mixed DM/non-DM modules).
            await _interactions.RegisterCommandsGloballyAsync();

            await PurgeStaleGuildCommandsAsync();
        }

        // [BuildConfigOnly] modules were just discovered/tracked in-memory by AddModulesAsync above;
        // pull the disallowed ones back out before RegisterCommandsGloballyAsync so they never reach
        // Discord's slash picker outside their allowed configs (no #if - see BuildConfig.cs).
        private async Task RemoveModulesDisallowedForCurrentBuildConfigAsync() {
            var disallowedTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.GetCustomAttribute<BuildConfigOnlyAttribute>() is { AllowsCurrent: false });

            foreach(var type in disallowedTypes) {
                var removed = await _interactions.RemoveModuleAsync(type);
                if(removed) _logger.LogInformation("Skipping registration of {module} - not allowed in {config}", type.Name, BuildConfig.Current);
            }
        }

        // This app only ever registers commands globally, so any guild-scoped command is a leftover
        // from an older deploy (e.g. the pre-InteractionService /addmerit with user1..user10 params)
        // shadowing the current global command of the same name. Self-heal by deleting them, guarded
        // against wiping something unexpected: skip and log loudly instead of deleting past a sane cap.
        private const int MaxStaleGuildCommandsPerGuild = 90;

        private async Task PurgeStaleGuildCommandsAsync() {
            foreach(var guild in _discord.Gateway.Guilds) {
                try {
                    var staleCommands = await guild.GetApplicationCommandsAsync();
                    if(staleCommands.Count == 0) continue;

                    if(staleCommands.Count > MaxStaleGuildCommandsPerGuild) {
                        _logger.LogWarning("Guild {guildId} has {count} guild-scoped commands, above the {cap} sanity cap - skipping auto-purge, check manually.",
                            guild.Id, staleCommands.Count, MaxStaleGuildCommandsPerGuild);
                        continue;
                    }

                    // RestGuildCommand (not SocketApplicationCommand) is what exposes GetCommandPermission,
                    // so fetch the REST view of this guild's commands once and match by ID before deleting.
                    var restCommands = await _discord.Gateway.Rest.GetGuildApplicationCommands(guild.Id);
                    foreach(var command in staleCommands) {
                        var restCommand = restCommands.FirstOrDefault(c => c.Id == command.Id);
                        if(restCommand is not null) await LogCommandPermissionsAsync(guild, restCommand);
                        _logger.LogWarning("Deleting stale guild-scoped command /{name} in guild {guildId}", command.Name, guild.Id);
                        await command.DeleteAsync();
                    }
                } catch(Exception e) {
                    _logger.LogError(e, "Failed purging stale guild-scoped commands for guild {guildId}", guild.Id);
                    _bugsnag.Notify(e);
                }
            }
        }

        // Best-effort snapshot so a permission wipe from the delete above can be manually replayed from
        // logs. Discord has no API to restore permissions after the command itself is gone, so this is
        // the only recovery path - not persisted anywhere structured on purpose, this is a safety net for
        // an operation that should be rare, not a feature to build workflows on top of.
        private async Task LogCommandPermissionsAsync(SocketGuild guild, RestGuildCommand command) {
            try {
                var permissions = await command.GetCommandPermission();
                if(permissions?.Permissions is { Count: > 0 }) {
                    var summary = string.Join(", ", permissions.Permissions.Select(p => $"{p.TargetType}:{p.TargetId}={p.Permission}"));
                    _logger.LogWarning("Permission overrides for /{name} in guild {guildId} before delete: {permissions}", command.Name, guild.Id, summary);
                }
            } catch(Exception e) {
                _logger.LogWarning(e, "Could not fetch permission overrides for /{name} in guild {guildId} before delete", command.Name, guild.Id);
            }
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
                        var dropComponent = ComponentsV2EmbedHelpers.Error("The bot is currently overloaded. Please try again in a moment.");
                        if(interaction.HasResponded) await interaction.ModifyOriginalResponseAsync(m => { m.Components = dropComponent; m.Flags = MessageFlags.ComponentsV2; });
                        else await interaction.RespondAsync(components: dropComponent, flags: MessageFlags.ComponentsV2, ephemeral: true);
                    } catch(Exception) { }
                    return;
                }
                Total.Inc();
                // Skip autocomplete from the command counter to match the legacy CommandService behavior
                // (autocomplete fires many times per typed character; counting it skews per-minute rates).
                if(interaction is SocketAutocompleteInteraction)
                    _logger.LogDebug("Autocomplete for {command} by {username}", CommandName(interaction), interaction.User?.Username);
                else {
                    RuntimeMetrics.AddCommands();
                    _logger.LogInformation("Running command {command}({params}) for user: {username} in guild {guild} channel {channel}",
                        CommandName(interaction), CommandParams(interaction), interaction.User?.Username, DescribeGuild(ctx.Guild), DescribeChannel(ctx.Channel));
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try {
                    await _interactions.ExecuteCommandAsync(ctx, _provider);
                } catch(Exception e) {
                    Failures.Inc();
                    RuntimeMetrics.AddCommandFailures();
                    _logger.LogError(e, "Interaction {command}({params}) threw for user {username} in guild {guild}",
                        CommandName(interaction), CommandParams(interaction), interaction.User?.Username, DescribeGuild(ctx.Guild));
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
            RuntimeMetrics.AddCommandFailures();
            var interaction = ctx.Interaction;
            MessageComponent component;
            if(result is ExecuteResult er && er.Exception is not null) {
                _logger.LogError(er.Exception, "Command {command}({params}) failed for {username} in guild {guild}",
                    info?.Name, CommandParams(interaction), interaction.User?.Username, DescribeGuild(ctx.Guild));
                _bugsnag.Notify(er.Exception);
                component = ComponentsV2EmbedHelpers.ExceptionFrame(er.Exception);
            } else {
                _logger.LogWarning("Command {command}({params}) failed for {username} in guild {guild}: {error} - {reason}",
                    info?.Name, CommandParams(interaction), interaction.User?.Username, DescribeGuild(ctx.Guild), result.Error, result.ErrorReason);
                component = ComponentsV2EmbedHelpers.Error(result.ErrorReason ?? "Command could not be completed.");
            }
            try {
                if(interaction.HasResponded) await interaction.ModifyOriginalResponseAsync(m => { m.Components = component; m.Flags = MessageFlags.ComponentsV2; });
                else await interaction.RespondAsync(components: component, flags: MessageFlags.ComponentsV2, ephemeral: true);
            } catch(Exception) { }
        }

        private static string CommandName(IDiscordInteraction interaction) => interaction switch {
            SocketSlashCommand s => string.Join(" ", new[] { s.Data.Name }.Concat(SubCommandPath(s.Data.Options))),
            SocketUserCommand u => u.Data.Name,
            SocketMessageComponent c => c.Data.CustomId,
            SocketModal m => m.Data.CustomId,
            SocketAutocompleteInteraction a => a.Data.CommandName,
            _ => interaction.Type.ToString()
        };

        private static IEnumerable<string> SubCommandPath(IReadOnlyCollection<SocketSlashCommandDataOption> options) {
            var sub = options?.FirstOrDefault(o => o.Type is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup);
            if(sub is null) yield break;
            yield return sub.Name;
            foreach(var nested in SubCommandPath(sub.Options)) yield return nested;
        }

        private static string CommandParams(IDiscordInteraction interaction) => interaction switch {
            SocketSlashCommand s => FormatSlashOptions(s.Data.Options),
            SocketMessageComponent { Data.Values.Count: > 0 } c => string.Join(", ", c.Data.Values),
            SocketModal m => string.Join(", ", m.Data.Components.Select(comp => $"{comp.CustomId}={comp.Value}")),
            _ => ""
        };

        private static string FormatSlashOptions(IReadOnlyCollection<SocketSlashCommandDataOption> options) {
            if(options is null) return "";
            var parts = new List<string>();
            foreach(var opt in options) {
                if(opt.Type is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup) parts.Add(FormatSlashOptions(opt.Options));
                else parts.Add($"{opt.Name}={FormatOptionValue(opt.Value)}");
            }
            return string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        private static string FormatOptionValue(object value) => value switch {
            null => "null",
            IUser u => $"{u.Username}({u.Id})",
            IChannel c => $"{c.Name}({c.Id})",
            IRole r => $"{r.Name}({r.Id})",
            IAttachment a => a.Filename,
            _ => value.ToString()
        };

        private static string DescribeGuild(IGuild guild) => guild is null ? "DM" : $"{guild.Name}({guild.Id})";
        private static string DescribeChannel(IMessageChannel channel) => channel is null ? "unknown" : $"{channel.Name}({channel.Id})";
    }
}
