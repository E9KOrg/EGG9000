using Discord;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    /// <summary>
    /// Refreshes <see cref="CoopStatsCache"/> and maintains the opt-in stats embeds:
    /// one server wide embed in the configured CoopStatsChannel, and one per contract
    /// embed (the "second embed") in each contract channel. Opt-in is having a
    /// GuildChannelType.CoopStatsChannel configured. All Discord writes route through
    /// the LOW queue so they never compete with interaction responses.
    /// </summary>
    public class CoopStatsRefreshService(IServiceProvider provider)
        : _UpdaterBase<CoopStatsRefreshService>(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(1), provider) {

        private const string MarkerAuthor = "Co-op Stats";

        private CoopStatsCache _statsCache => _provider.GetRequiredService<CoopStatsCache>();

        public async override Task Run(object state, CancellationToken cancellationToken) {
            await _statsCache.RefreshAsync();

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guilds = await db.Guilds.ToListAsync(CancellationToken.None);
            // Two independent opt-ins: a configured CoopStatsChannel enables the
            // server-wide embed; the ShowContractStatsEmbeds toggle (default off) enables
            // the per-contract embed inside each contract channel.
            var optedIn = guilds
                .Where(g => g.HasChannel(GuildChannelType.CoopStatsChannel)
                         || g.ShowContractStatsEmbeds)
                .ToList();
            if(optedIn.Count == 0)
                return;

            var contractEmbedGuildIds = optedIn
                .Where(g => g.ShowContractStatsEmbeds)
                .Select(g => g.Id).ToList();
            var guildContracts = await db.GuildContracts.Include(gc => gc.Contract)
                .Where(gc => !gc.DeletedChannel && contractEmbedGuildIds.Contains(gc.GuildID))
                .ToListAsync(CancellationToken.None);

            foreach(var guild in optedIn) {
                if(cancellationToken.IsCancellationRequested)
                    break;

                var statsChannelId = guild.GetChannelId(GuildChannelType.CoopStatsChannel);
                if(statsChannelId is > 0 && _client.Gateway.GetChannel(statsChannelId.Value) is IMessageChannel statsChannel) {
                    var server = _statsCache.GetServerStats(guild.Id);
                    await MaintainEmbed(statsChannel, BuildServerEmbed(server));
                }

                if(!guild.ShowContractStatsEmbeds)
                    continue;

                foreach(var gc in guildContracts.Where(gc => gc.GuildID == guild.Id)) {
                    if(cancellationToken.IsCancellationRequested)
                        break;
                    var stats = _statsCache.GetContractStats(guild.Id, gc.ContractID);
                    if(stats is null)
                        continue;
                    if(_client.Gateway.GetChannel(gc.DiscordChannelId) is IMessageChannel contractChannel) {
                        await MaintainEmbed(contractChannel, BuildContractEmbed(stats));
                    }
                }
            }
        }

        // Separator between the stats body and the "last updated" subtext line. The
        // body before this marker is what drives change detection; the timestamp line
        // after it is client-rendered (relative) and excluded from comparison.
        private const string TimestampSep = "\n\n-# ";

        // Live, client side relative timestamp ("a few seconds ago" / "5 minutes ago")
        // as Discord subtext. Reflects the last time the stats body actually changed,
        // since the embed is only rewritten on change.
        private string TimestampLine() {
            var unix = (_statsCache.LastRefresh ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            return $"{TimestampSep}Last updated <t:{unix}:R>";
        }

        private Embed BuildServerEmbed(ServerStats s) {
            var body = s is null
                ? "No active co-op data yet."
                : $"Active contracts: **{s.ActiveContracts}**\n" +
                  $"Active co-ops: **{s.ActiveCoops}**\n" +
                  $"Pending creation: **{s.PendingThreads}**\n" +
                  $"Players in co-ops: **{s.UsersAssigned}**\n" +
                  $"Finished (30d): **{s.FinishedCoops}**";

            return new EmbedBuilder()
                .WithAuthor(MarkerAuthor)
                .WithTitle("Server Co-op Stats")
                .WithDescription(body + TimestampLine())
                .WithColor(Color.Blue)
                .Build();
        }

        private Embed BuildContractEmbed(ContractStats s) {
            var body =
                $"Active co-ops: **{s.ActiveCoops}**\n" +
                $"Pending creation: **{s.PendingThreads}**\n" +
                $"Players assigned: **{s.UsersAssigned}**\n" +
                $"Average fill: **{s.AverageFill:P0}**\n" +
                $"Finished (30d): **{s.FinishedCoops}**";

            return new EmbedBuilder()
                .WithAuthor(MarkerAuthor)
                .WithTitle($"{s.ContractName} - Co-op Stats")
                .WithDescription(body + TimestampLine())
                .WithColor(Color.Blue)
                .Build();
        }

        // The stats portion of a description, excluding the trailing timestamp subtext.
        private static string StatsBody(string description) =>
            description is null ? null : description.Split(TimestampSep)[0];

        /// <summary>
        /// Finds this bot's existing stats message (by marker author) in the channel
        /// and edits it when the body changed, otherwise posts a new one. The footer
        /// (timestamp) is ignored for change detection so idle contracts do not
        /// generate needless edits. Reads are direct; the write goes through the queue.
        /// </summary>
        private async Task MaintainEmbed(IMessageChannel channel, Embed embed) {
            try {
                var recent = (await channel.GetMessagesAsync(limit: 25).FlattenAsync()).ToList();
                var existing = recent
                    .OfType<IUserMessage>()
                    .FirstOrDefault(m => m.Author.Id == _client.Gateway.CurrentUser.Id
                                      && m.Embeds.Any(e => e.Author?.Name == MarkerAuthor));

                if(existing is null) {
                    _queue.EnqueueLow(() => channel.SendMessageAsync(embed: embed));
                    return;
                }

                var current = existing.Embeds.FirstOrDefault(e => e.Author?.Name == MarkerAuthor);
                if(StatsBody(current?.Description) == StatsBody(embed.Description) && current?.Title == embed.Title)
                    return; // stats body unchanged (ignoring timestamp), skip edit

                var capturedExisting = existing;
                _queue.EnqueueLow(() => capturedExisting.ModifyAsync(m => m.Embed = embed));
            } catch(Exception e) {
                _logger.LogError(e, "Failed maintaining stats embed in channel {Channel}", channel?.Id);
            }
        }
    }
}
