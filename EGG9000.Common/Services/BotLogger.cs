using Discord;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class BotLogger(DiscordSocketClient discord, IServiceProvider provider, Bugsnag.IClient bugsnag) {
        private readonly DiscordSocketClient _discord = discord;
        private readonly IServiceProvider _provider = provider;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;

        public class BoardingGroupStatus {
            public int Num { get; set; }
            public string ContractId { get; set; }
            public string ContractName { get; set; }
            public ulong GuildId { get; set; }
            public RestUserMessage Message { get; set; }
            public DateTimeOffset Since { get; set; }
            public ulong ChannelId { get; set; }
            public string EggImageUrl { get; set; }
            public bool Assigning { get; set; } = true;
            public int CoopCount { get; set; }
            public int StartedCount { get; set; }
            public int ThreadCreatedCount { get; set; }
            public SemaphoreSlim Gate { get; } = new(1, 1);
        }

        private readonly ConcurrentDictionary<(int Num, string ContractId, ulong GuildId), BoardingGroupStatus> _boardingGroups = new();

        public async Task Log(string message, ulong guildId) {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guild = db.CachedGuilds.FirstOrDefault(g => g.Id == guildId);
            if(guild is null) return;
            _ = await ChannelHelper.DetermineAndSend(_discord, guild, GuildChannelType.BotLog, new() { Text = message });
        }
        public async Task Log(string message, Guild guild) {
            _ = await ChannelHelper.DetermineAndSend(_discord, guild, GuildChannelType.BotLog, new() { Text = message });
        }

        public async Task AddBoardingGroup(int bgnum, Contract contract, Guild guild) {
            try {
                var channel = await ChannelHelper.GetTextChannel(_discord, guild, GuildChannelType.BotLog);
                if(channel is null) return;

                ulong channelId = 0;
                string eggImageUrl = null;
                try {
                    using var scope = _provider.CreateScope();
                    var scopedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var guildContract = await scopedDb.GuildContracts.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.ContractID == contract.ID && x.GuildID == guild.Id && x.League == 0);
                    channelId = guildContract?.DiscordChannelId ?? 0;
                    if(contract.Details is not null) {
                        var customEggs = await scopedDb.GetCustomEggsAsync();
                        eggImageUrl = EggIncStatics.GetEggByContract(contract, customEggs)?.image;
                    }
                } catch(Exception ex) {
                    _bugsnag.Notify(ex);
                }

                var status = new BoardingGroupStatus {
                    Num = bgnum,
                    ContractId = contract.ID,
                    ContractName = contract.Name,
                    GuildId = guild.Id,
                    Since = DateTimeOffset.Now,
                    Assigning = true,
                    ChannelId = channelId,
                    EggImageUrl = eggImageUrl
                };

                status.Message = await channel.SendMessageAsync(embed: GenerateBoardingGroupEmbed(status));
                _boardingGroups[(bgnum, contract.ID, guild.Id)] = status;
            } catch(Exception ex) {
                _bugsnag.Notify(ex);
            }
        }

        public Task RefreshBoardingGroup(int bgnum, string contractid, ulong guildId)
            => RefreshAndRender(bgnum, contractid, guildId, markAssigned: false);

        public Task MarkAssigned(int bgnum, string contractid, ulong guildId)
            => RefreshAndRender(bgnum, contractid, guildId, markAssigned: true);

        private async Task RefreshAndRender(int bgnum, string contractid, ulong guildId, bool markAssigned) {
            var key = (bgnum, contractid, guildId);
            if(!_boardingGroups.TryGetValue(key, out var status) || status.Message is null) return;

            await status.Gate.WaitAsync();
            try {
                if(markAssigned) status.Assigning = false;

                using var scope = _provider.CreateScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var group = (ulong)bgnum;
                var since = status.Since.AddMinutes(-5);
                var coops = await scopedDb.Coops.AsNoTracking()
                    .Where(c => c.ContractID == contractid && c.GuildId == guildId && c.Group == group
                        && c.Created >= since && c.Status != CoopStatusEnum.Failed)
                    .Select(c => new { c.Status, c.ThreadID })
                    .ToListAsync();

                status.CoopCount = coops.Count;
                status.StartedCount = coops.Count(c => (int)c.Status >= (int)CoopStatusEnum.WaitingOnThread);
                status.ThreadCreatedCount = coops.Count(c => c.ThreadID != 0);

                await status.Message.ModifyAsync(m => m.Embed = GenerateBoardingGroupEmbed(status));

                if(!status.Assigning && status.CoopCount > 0
                    && status.StartedCount >= status.CoopCount && status.ThreadCreatedCount >= status.CoopCount) {
                    _boardingGroups.TryRemove(key, out _);
                }
            } catch(Exception ex) {
                _bugsnag.Notify(ex);
            } finally {
                status.Gate.Release();
            }
        }

        public static Embed GenerateBoardingGroupEmbed(BoardingGroupStatus status) {
            var coopCountText = status.Assigning ? "Currently Assigning..." : status.CoopCount.ToString();
            var complete = !status.Assigning && status.CoopCount > 0 && status.ThreadCreatedCount >= status.CoopCount;
            var allMatch = !status.Assigning && status.CoopCount > 0
                && status.StartedCount == status.CoopCount && status.ThreadCreatedCount == status.CoopCount;

            var lastUpdated = DiscordHelpers.TimeStamper(DateTimeOffset.Now);
            var body = allMatch
                ? $"Coop Count: {status.CoopCount}\nAll co-ops created and started"
                : $"Coop Count: {coopCountText}\nStarted Count: {status.StartedCount}\nThread Created Count: {status.ThreadCreatedCount}";

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"{status.ContractName}, BG{status.Num}")
                .WithDescription($"{body}\n\n-# Last updated {lastUpdated}")
                .WithColor(complete ? Color.Green : new Color(255, 255, 0));

            if(!string.IsNullOrEmpty(status.EggImageUrl))
                embedBuilder.WithThumbnailUrl(status.EggImageUrl);
            if(status.ChannelId != 0)
                embedBuilder.AddField("Channel", $"<#{status.ChannelId}>", inline: true);

            return embedBuilder.Build();
        }
    }
}
