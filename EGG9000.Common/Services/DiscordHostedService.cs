using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class DiscordHostedService : DiscordSocketClient {
        public bool IsReady { get; private set; }
        private IConfiguration _configuration;
        private ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private IServiceProvider _provider;
        private ILogger<DiscordHostedService> _logger;
        private static DiscordSocketConfig config = new DiscordSocketConfig() {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.Guilds | GatewayIntents.GuildMessages | 
                             GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
        };
        public DiscordHostedService(IConfiguration Configuration, IMemoryCache cache, IServiceProvider provider, ILogger<DiscordHostedService> logger) : base(config) {
            _configuration = Configuration;
            _provider = provider;
            _logger = logger;

            this.Log += PrintLog;
            this.Ready += DiscordHostedService_Ready;
            this.LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();
            this.StartAsync().Wait();

            _logger.Log(LogLevel.Information, "Waiting on Discord Connect");

            while(this.ConnectionState != ConnectionState.Connected) {
                
             }

            _logger.Log(LogLevel.Information, "Discord Ready");

            _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            _cache = cache;
        }

        //public Task StartAsync(CancellationToken cancellationToken) {
        //    return Task.CompletedTask;
        //}

        //public async Task StopAsync(CancellationToken cancellationToken) {
        //    _logger.LogInformation("Stopping Discord Client");
        //    await this.StopAsync();
        //    while(this.ConnectionState != ConnectionState.Disconnected) { }
        //    _logger.LogInformation("Discord Client Stopped");
        //}


        private Task DiscordHostedService_Ready() {
            IsReady = true;
            this.SetGameAsync("").Wait();

            foreach(var guild in this.Guilds) {
                _logger.Log(LogLevel.Information, "Download guild users for {Guild}", guild.Name);

                guild.DownloadUsersAsync().Wait();
            }

            _logger.Log(LogLevel.Information, "Discord Ready");

            return Task.CompletedTask;
        }

        private Task PrintLog(LogMessage msg) {
            if(msg.ToString().Contains("Rate limit triggered")) {
                _logger.Log(LogLevel.Trace, "Discord Log: {msg}", msg.Message);
            } else if(msg.Exception is not null) {
                _logger.LogError(msg.Exception, "Discord Log: Exception Thrown");
            } else {
                _logger.Log(LogLevel.Information, "Discord Log: {msg}", msg.Message);
            }
            return Task.CompletedTask;
        }

        public static string DBGuildsKey = "DBGuildsCache";

        private async Task<Guild> GetDbGuild(SocketGuild guild) {
            if(!_cache.TryGetValue(DBGuildsKey, out List<Guild> guildData)) {
                var db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
                return dbguild.FinishedCategories.Split(",").Select(x => guild.CategoryChannels.FirstOrDefault(y => y.Id.ToString() == x)).Where(x => x is not null).ToList();
            } else {
                var categories = guild.CategoryChannels.Where(x => x.Name != null).Where(x => x.Name.ToLower().Contains("finished") && x.Name.ToLower().Contains("coops")).OrderBy(x => x.Position);
                return categories.Where(x => x is not null).ToList();
            }
        }

        public async Task<SocketTextChannel> GetChannelAsync(GuildChannelType channelType, SocketGuild guild) {
            return await GetChannelOrCategory<SocketTextChannel>(channelType, guild);
        }
        public async Task<SocketCategoryChannel> GetCategoryAsync(GuildChannelType channelType, SocketGuild guild) {
            return await GetChannelOrCategory<SocketCategoryChannel>(channelType, guild);
        }
        public async Task<SocketRole> GetRoleAsync(GuildChannelType channelType, SocketGuild guild) {
            var dbguild = await GetDbGuild(guild);

            var channelDetails = dbguild.ChannelDetails;
            var channelDetail = channelDetails.FirstOrDefault(x => x.ChannelType == channelType);
            if(channelDetail == null)
                return null ;

            return guild.GetRole(channelDetail.Id);
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
