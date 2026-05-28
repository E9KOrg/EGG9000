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
        public BotLogger (DiscordSocketClient discord, ApplicationDbContext db) {
            _discord = discord;
            _db = db;
        }


        public async Task Log(string message, ulong guildId) {
            var guild = _db.CachedGuilds.FirstOrDefault(g => g.Id == guildId);
            if(guild is null) return;
            _ = await ChannelHelper.DetermineAndSend(_discord, guild, GuildChannelType.BotLog, new() { Text = message });
        }
        public async Task Log(string message, Guild guild) {
            _ = await ChannelHelper.DetermineAndSend(_discord, guild, GuildChannelType.BotLog, new() { Text = message });
        }


    }
}
