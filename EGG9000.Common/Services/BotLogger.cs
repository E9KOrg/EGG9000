using Discord;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class BotLogger {
        private readonly DiscordSocketClient _discord;
        private readonly ApplicationDbContext _db;
        private readonly Bugsnag.IClient _bugsnag;
        public BotLogger(DiscordSocketClient discord, ApplicationDbContext db, Bugsnag.IClient bugsnag) {
            _discord = discord;
            _db = db;
            _bugsnag = bugsnag;
        }


        public class BoardingGroupStatus {
            public int Num { get; set; }
            public string ContractId { get; set; }
            public string ContractName { get; set; }
            public ulong GuildId { get; set; }
            public RestUserMessage Message { get; set; }
            public int CoopCount { get; set; }
            public int StartedCount { get; set; }
            public int ThreadCreatedCount { get; set; }
        }

        public List<BoardingGroupStatus> BoardingGroupStatuses { get; set; } = new List<BoardingGroupStatus>();

        public async Task Log(string message, ulong guildId) {
            var guild = _db.CachedGuilds.FirstOrDefault(g => g.Id == guildId);
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

                var status = new BoardingGroupStatus {
                    Num = bgnum,
                    ContractId = contract.ID,
                    ContractName = contract.Name,
                    GuildId = guild.Id,
                    CoopCount = -1,
                    StartedCount = 0,
                    ThreadCreatedCount = 0
                };

                status.Message = await channel.SendMessageAsync(embed: GenerateBoardingGroupEmbed(status));
                BoardingGroupStatuses.Add(status);
            } catch(Exception ex) {
                _bugsnag.Notify(ex);
            }
        }

        public async Task UpdateBoardingGroup(int bgnum, string contractid, ulong guildId, int? coopCount, int? startedCount, int? threadCreatedCount) {
            try {
                var status = BoardingGroupStatuses.FirstOrDefault(s => s.Num == bgnum && s.ContractId == contractid && s.GuildId == guildId);
                if(status?.Message is null) return;
                status.CoopCount = coopCount ?? status.CoopCount;
                status.StartedCount = startedCount ?? status.StartedCount;
                status.ThreadCreatedCount = threadCreatedCount ?? status.ThreadCreatedCount;
                await status.Message.ModifyAsync(m => m.Embed = GenerateBoardingGroupEmbed(status));
                if(status.StartedCount == status.CoopCount && status.ThreadCreatedCount == status.CoopCount) {
                    BoardingGroupStatuses.Remove(status);
                }
            } catch(Exception ex) {
                _bugsnag.Notify(ex);
            }
        }

        public Embed GenerateBoardingGroupEmbed(BoardingGroupStatus status) {
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"Contract: {status.ContractName}, BG{status.Num}")
                .WithDescription($"\nCoop Count: {(status.CoopCount >= 0 ? status.CoopCount.ToString() : "Currently Assigning...")}\nStarted Count: {status.StartedCount}\nThread Created Count: {status.ThreadCreatedCount}")
                .WithColor(status.ThreadCreatedCount < status.CoopCount ? new Color(255, 255, 0) : Color.Green);

            return embedBuilder.Build();
        }
    }
}
