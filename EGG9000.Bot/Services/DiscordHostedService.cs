using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class DiscordHostedService : DiscordSocketClient {
        public bool IsReady { get; private set; }
        private IConfiguration _configuration;
        private ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private static DiscordSocketConfig config = new DiscordSocketConfig() {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessages
        };
        public DiscordHostedService(IConfiguration Configuration, ApplicationDbContext db, IMemoryCache cache) : base(config) {
            _configuration = Configuration;
            

            this.Log += PrintLog;
            this.Ready += DiscordHostedService_Ready;

            this.LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();
            this.StartAsync().Wait();

            Console.WriteLine("Waiting on Discord Connect");

            while(this.ConnectionState != ConnectionState.Connected) { }
            
            Console.WriteLine("Waiting on Discord Ready");

            _db = db;
            _cache = cache;
        }

        private Task DiscordHostedService_Ready() {
            IsReady = true;
            this.SetGameAsync("").Wait();

            foreach(var guild in this.Guilds) {
                Console.WriteLine($"Downloading guild users for {guild.Name}");
                guild.DownloadUsersAsync().Wait();
            }

            Console.WriteLine("Discord Ready");

            return Task.CompletedTask;
        }

        private Task PrintLog(LogMessage msg) {
            if(!msg.ToString().Contains("Rate limit triggered")) {
                Console.WriteLine(msg.ToString());
            }
            return Task.CompletedTask;
        }

        public static string DBGuildsKey = "DBGuildsCache";

        private async Task<Guild> GetDbGuild(SocketGuild guild) {
            if(!_cache.TryGetValue(DBGuildsKey, out List<Guild> guildData)) {
                var db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
                guildData = await db.Guilds.ToListAsync();
                _cache.Set(DBGuildsKey, guildData, TimeSpan.FromMinutes(30));
            }

            return guildData.First(x => x.Id == guild.Id || x.OverflowServers.Any(y => y == guild.Id));
        }
        public async Task<List<SocketCategoryChannel>> GetAllCoopCategories(SocketGuild guild) {
            var dbguild = await GetDbGuild(guild);
            if(guild.Id == dbguild.Id) {
                return dbguild.CoopCategories.Split(",").Select(x => guild.CategoryChannels.FirstOrDefault(y => y.Id.ToString() == x)).ToList();
            } else {
                var categories = guild.CategoryChannels.Where(x => x.Name != null).Where(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && !x.Name.ToLower().Contains("finished") && !x.Name.ToLower().Contains("failed")).OrderBy(x => x.Position);
                return categories.ToList();
            }
        }
        public async Task<List<SocketCategoryChannel>> GetAllFinishedCategories(SocketGuild guild) {
            var dbguild = await GetDbGuild(guild);
            if(guild.Id == dbguild.Id) {
                return dbguild.FinishedCategories.Split(",").Select(x => guild.CategoryChannels.FirstOrDefault(y => y.Id.ToString() == x)).ToList();
            } else {
                var categories = guild.CategoryChannels.Where(x => x.Name != null).Where(x => x.Name.ToLower().Contains("finished") && x.Name.ToLower().Contains("coops")).OrderBy(x => x.Position);
                return categories.ToList();
            }
        }

        public async Task<SocketTextChannel> GetChannelAsync(GuildChannelType channelType, SocketGuild guild) {
            return await GetChannelOrCategory<SocketTextChannel>(channelType, guild);
        }
        public async Task<SocketCategoryChannel> GetCategoryAsync(GuildChannelType channelType, SocketGuild guild) {
            return await GetChannelOrCategory<SocketCategoryChannel>(channelType, guild);
        }
        private async Task<T> GetChannelOrCategory<T>(GuildChannelType channelType, SocketGuild guild) where T : IGuildChannel {
            var dbguild = await GetDbGuild(guild);

            var channelDetails = dbguild.ChannelDetails;
            var channelDetail = channelDetails.FirstOrDefault(x => x.ChannelType == channelType);
            if(channelDetail == null)
                return default(T);

            if(channelType.ToString().Contains("Category"))
                return (T)Convert.ChangeType(guild.CategoryChannels.FirstOrDefault(x => x.Id == channelDetail.Id), typeof(T));

            return (T)Convert.ChangeType(guild.TextChannels.FirstOrDefault(x => x.Id == channelDetail.Id), typeof(T));
        }

    }
}
