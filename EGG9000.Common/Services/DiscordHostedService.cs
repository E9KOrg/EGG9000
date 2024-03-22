using Bugsnag;
using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Joins;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class DiscordHostedService : DiscordSocketClient {
        public bool IsReady { get; private set; }
        private Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private IServiceProvider _provider;
        private ILogger<DiscordHostedService> _logger;
        private static DiscordSocketConfig config = new DiscordSocketConfig() {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.Guilds | GatewayIntents.GuildMessages | 
                             GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
        };
        private static readonly List<DiscordSemahpore> _serverSemaphores = [];
        private static readonly TimeSpan _semaphoreTimeoutTime = TimeSpan.FromMinutes(3);
        public DiscordHostedService(Microsoft.Extensions.Configuration.IConfiguration Configuration, IMemoryCache cache, IServiceProvider provider, ILogger<DiscordHostedService> logger) : base(config) {
            _configuration = Configuration;
            _provider = provider;
            _logger = logger;

            Log += PrintLog;
            Ready += DiscordHostedService_Ready;
            LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();
            StartAsync().Wait();

            _logger.Log(LogLevel.Information, "Waiting on Discord Connect");
            while(ConnectionState != ConnectionState.Connected) {}
            _logger.Log(LogLevel.Information, "Discord Ready");

            _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            _cache = cache;

            foreach(var guild in Guilds) {
                _serverSemaphores.Add(new DiscordSemahpore(guild, new(1, 1)));
            }
        }

        public class RestartDiscordExecption(string customMessage, Severity severity) : Exception {
            public string CustomMessage { get; set; } = customMessage;
            public Severity Severity { get; set; } = severity;
        }

        public Task RestartAsync() {
            if(ConnectionState != ConnectionState.Connected) {
                throw new RestartDiscordExecption("Not connected yet - cannot restart.", Severity.Warning);
            }

            try {
                //Logout subtasks
                LogoutAsync();
                StopAsync().Wait();
                _logger.Log(LogLevel.Information, "Waiting on Discord Disconnect");
                while(ConnectionState == ConnectionState.Connected) { }
                _logger.Log(LogLevel.Information, "Discord Disconnected...");

                //Log back in
                LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();
                StartAsync().Wait();
                _logger.Log(LogLevel.Information, "Waiting on Discord Connect");
                while(ConnectionState != ConnectionState.Connected) { }
                _logger.Log(LogLevel.Information, "Discord Ready");
            } catch(Exception ex) {
                throw new RestartDiscordExecption(ex.Message, Severity.Error);
            }

            return Task.CompletedTask;
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
                return dbguild.CoopCategories.Split(",").Select(x => guild.CategoryChannels.FirstOrDefault(y => y.Id.ToString() == x)).Where(x => x is not null).ToList();
            } else {
                var categories = guild.CategoryChannels.Where(x => x.Name != null).Where(x => (x.Name.ToLower().Contains("coops") || x.Name.ToLower().Contains("co-ops")) && !x.Name.ToLower().Contains("finished") && !x.Name.ToLower().Contains("failed")).OrderBy(x => x.Position);
                return categories.Where(x => x is not null).ToList();
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
        public async Task<SocketTextChannel> GetChannelAsync(GuildChannelType channelType, Guild guild) {
            return await GetChannelOrCategory<SocketTextChannel>(channelType, GetGuild(guild.Id));
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
            try {
                var dbguild = await GetDbGuild(guild);

                var channelDetails = dbguild.ChannelDetails;
                var channelDetail = channelDetails.FirstOrDefault(x => x.ChannelType == channelType);
                if(channelDetail == null || channelDetail.Id == 0)
                    return default;

                return (T)Convert.ChangeType(GetChannel(channelDetail.Id), typeof(T));
            }catch (Exception e) {
                _logger.LogError(e, "Error getting channel or category");
                return default;
            }
        }
        public static List<DiscordSemahpore> GetSemaphores() {
            return _serverSemaphores;
        }
        public static TimeSpan GetSemaphoreTimeout() {
            return _semaphoreTimeoutTime;
        }
    }
    public class DiscordSemahpore(SocketGuild guild, SemaphoreSlim semaphore) {
        public readonly SocketGuild Guild = guild;
        public readonly SemaphoreSlim Semaphore = semaphore;
    }

    public static class DiscordExtensions {

        public static SemaphoreSlim GetServerSemaphore(this SocketGuild guild) {
            return DiscordHostedService.GetSemaphores().FirstOrDefault(s => s.Guild == guild).Semaphore;
        }

        public static List<IChannel> GetInUseChannels(this SocketGuild guild, SocketGuildChannel category = null) {
            return guild.Channels.Where(c =>
                (c.GetChannelType() == ChannelType.Category ||
                c.GetChannelType() == ChannelType.Text ||
                c.GetChannelType() == ChannelType.Voice ||
                c.GetChannelType() == ChannelType.Store ||
                c.GetChannelType() == ChannelType.Forum ||
                c.GetChannelType() == ChannelType.News ||
                c.GetChannelType() == ChannelType.Media ||
                c.GetChannelType() == ChannelType.Stage)
                && (
                    category is null || (
                        (c as SocketTextChannel)?.CategoryId == category?.Id ||
                        (c as SocketTextChannel)?.Category == category
                    )
                )
            ).Select(c => c as IChannel).ToList();
        }

        public static int GetInUseChannelCount(this SocketGuild guild, SocketGuildChannel category = null) {
            return guild.GetInUseChannels(category).Count;
        }

        public static List<SocketThreadChannel> GetInUseThreads(this SocketGuild guild, SocketGuildChannel parentChannel = null) {
            return guild.ThreadChannels.Where(t =>
                !t.IsArchived &&
                (t.ParentChannel == parentChannel || parentChannel == null)
            ).ToList();
        }

        public static int GetInUseThreadCount(this SocketGuild guild, SocketGuildChannel parentChannel = null) {
            return guild.GetInUseThreads(parentChannel).Count;
        }

        public static async Task<SocketGuildChannel> CreateCoopThreadHeaderAsync(this SocketGuild guild, SocketRole leagueRole, Embed contractEmbed, SocketGuildChannel category, Coop coop) {
            if(category is null || category.Id  == 0) return null;

            var name = $"{coop.Contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(coop.League).ToLower()}";

            //Catch possible dupes before they happen
            Thread.Sleep(1000);

            if(guild.Channels.Any(c => c.Name == name)) return guild.Channels.First(c => c.Name == name);

            var channel = await guild.CreateTextChannelAsync(
                name,
                p => {p.CategoryId = category.Id;}
            );
            if(channel is null) return null;
            await channel.SendMessageAsync(text: "", embed: contractEmbed);

            if(leagueRole != null) {
                await channel.AddPermissionOverwriteAsync(leagueRole, new OverwritePermissions(viewChannel: PermValue.Allow));
            }

            return guild.GetChannel(channel.Id);
        }

        public static async Task DeleteCoopThreadHeadersAsync(this Guild guild, DiscordSocketClient client, Contract contract) {
            List<SocketGuild> guilds = [
                client.GetGuild(guild.DiscordSeverId),
                .. guild.OverflowServers.Select(client.GetGuild).ToList()
            ];

            foreach(var sg in guilds) {
                var channels = sg.TextChannels.Where(c => c.Name.StartsWith(contract.GetE9KName().ToLower()) && Regex.IsMatch(c.Name, @"(-aaa|-aa|-a|-b|-c)$"));
                foreach(var channel in channels) {
                    await channel.DeleteAsync();
                }
            }
        }

        public static async Task<List<IGuildUser>> ExtGetUsersAsync (this IThreadChannel channel) {
            return (await AsyncEnumerableExtensions.FlattenAsync(channel.GetUsersAsync())).ToList();
        }

        public static string GetE9KName(this Contract contract, bool toLower = true) {
            return (toLower ? contract.Name.ToLower() : contract.Name).Split(":").Last().Trim().Replace(" ", "-");
        }

        public static async Task<SocketTextChannel> GetParentChannelAsync(this IThreadChannel threadChannel) {
            try {
                return (await threadChannel.Guild.GetTextChannelAsync(threadChannel.CategoryId ?? ulong.MaxValue)) as SocketTextChannel ?? null;
            } catch(Exception) {
                return null;
            }
        }
    }
}
