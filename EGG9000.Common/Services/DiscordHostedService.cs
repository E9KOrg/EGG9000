using Bugsnag;

using Discord;
using Discord.WebSocket;

using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static Ei.ArtifactSpec.Types;

namespace EGG9000.Common.Services {
    public class DiscordHostedService : DiscordSocketClient {
        public bool IsReady { get; private set; }
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IServiceProvider _provider;
        private readonly ILogger<DiscordHostedService> _logger;
        private static readonly DiscordSocketConfig config = new() {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.Guilds | GatewayIntents.GuildMessages |
                             GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
        };
        private static readonly List<DiscordSemaphore> _serverSemaphores = [];
        private static readonly TimeSpan _semaphoreTimeoutTime = TimeSpan.FromMinutes(1);
        public DiscordHostedService(Microsoft.Extensions.Configuration.IConfiguration Configuration, IMemoryCache cache, IServiceProvider provider, ILogger<DiscordHostedService> logger) : base(config) {
            _configuration = Configuration;
            _provider = provider;
            _logger = logger;

            Log += PrintLog;
            Ready += DiscordHostedService_Ready;
            LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();
            StartAsync().Wait();

            _logger.Log(LogLevel.Information, "Waiting on Discord Connect");
            while(ConnectionState != ConnectionState.Connected) { }
            _logger.Log(LogLevel.Information, "Discord Ready");

            _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            _cache = cache;

            foreach(var guild in Guilds) {
                _serverSemaphores.Add(new DiscordSemaphore(guild, new(1, 1)));
            }
        }

        public class RestartDiscordException(string customMessage, Severity severity) : Exception {
            public string CustomMessage { get; set; } = customMessage;
            public Severity Severity { get; set; } = severity;
        }

        public async Task RestartAsync() {
            if(ConnectionState != ConnectionState.Connected) {
                throw new RestartDiscordException("Not connected yet - cannot restart.", Severity.Warning);
            }

            try {
                //Logout subtasks
                await LogoutAsync();
                await StopAsync();
                _logger.Log(LogLevel.Information, "Waiting on Discord Disconnect");
                while(ConnectionState == ConnectionState.Connected) { }
                _logger.Log(LogLevel.Information, "Discord Disconnected...");

                //Log back in
                await LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]);
                await StartAsync();
                _logger.Log(LogLevel.Information, "Waiting on Discord Connect");
                while(ConnectionState != ConnectionState.Connected) { }
                _logger.Log(LogLevel.Information, "Discord Ready");
            } catch(Exception ex) {
                throw new RestartDiscordException(ex.Message, Severity.Error);
            }

            return;
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
            SetGameAsync("").Wait();

            foreach(var guild in Guilds) {
                _logger.Log(LogLevel.Information, "Download guild users for {Guild}", guild.Name);

                guild.DownloadUsersAsync().Wait();
            }

            _logger.Log(LogLevel.Information, "Discord Ready");

            return Task.CompletedTask;
        }

        private Task PrintLog(LogMessage msg) {
            if(msg.Exception is not null) {
                _logger.LogError(msg.Exception, "Discord Log: Exception Thrown");
            } else {
                _logger.LogInformation("Discord Log: {msg}", msg.Message);
            }
            return Task.CompletedTask;
        }

        public static readonly string DBGuildsKey = "DBGuildsCache";

        private readonly SemaphoreSlim _dbGuildsKeySemaphore = new SemaphoreSlim(1, 1);
        private async Task<Guild> GetDbGuild(SocketGuild guild) {
            await _dbGuildsKeySemaphore.WaitAsync();
            try {
                if(!_cache.TryGetValue(DBGuildsKey, out List<Guild> guildData)) {
                    guildData = await _db.Guilds.ToListAsync();
                    _cache.Set(DBGuildsKey, guildData, TimeSpan.FromMinutes(30));
                }
                _dbGuildsKeySemaphore.Release();
                return guildData.First(x => x.Id == guild.Id || x.OverflowServers.Any(y => y == guild.Id));
            } catch(Exception) {
                _dbGuildsKeySemaphore.Release();
                throw;
            }
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
            var role = channelDetail == null ? null : guild.GetRole(channelDetail.Id);
            if(role == null && channelDetail != null) {
                var mainRole = Guilds.SelectMany(x => x.Roles.Where(x => x.Id == channelDetail.Id)).FirstOrDefault();
                if(mainRole is not null) {
                    role = guild.Roles.FirstOrDefault(x => x.Name == mainRole.Name);
                }
            }
            return role;
        }
        private async Task<T> GetChannelOrCategory<T>(GuildChannelType channelType, SocketGuild guild) where T : IGuildChannel {
            try {
                var dbguild = await GetDbGuild(guild);

                var channelDetails = dbguild.ChannelDetails;
                var channelDetail = channelDetails.FirstOrDefault(x => x.ChannelType == channelType);
                if(channelDetail == null || channelDetail.Id == 0)
                    return default;

                return (T)Convert.ChangeType(GetChannel(channelDetail.Id), typeof(T));
            } catch(Exception e) {
                _logger.LogError(e, "Error getting channel or category");
                return default;
            }
        }
        public static SemaphoreSlim GetOrCreateSemaphore(SocketGuild guild) {
            var semaphore = _serverSemaphores.FirstOrDefault(s => s.Guild == guild);
            if(semaphore is null) {
                semaphore = new DiscordSemaphore(guild, new(1, 1));
                _serverSemaphores.Add(semaphore);
            }

            return semaphore.Semaphore;
        }
        public static TimeSpan GetSemaphoreTimeout() {
            return _semaphoreTimeoutTime;
        }
    }
    public class DiscordSemaphore(SocketGuild guild, SemaphoreSlim semaphore) {
        public readonly SocketGuild Guild = guild;
        public readonly SemaphoreSlim Semaphore = semaphore;
    }

    public static class DiscordExtensions {

        public static SemaphoreSlim GetServerSemaphore(this SocketGuild guild) {
            return DiscordHostedService.GetOrCreateSemaphore(guild);
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

        public static async Task<SocketGuildChannel> CreateCoopThreadHeaderAsync(this SocketGuild guild, SocketRole leagueRole, List<SocketRole> ultraRoles, Embed contractEmbed, SocketGuildChannel category, uint league, Contract contract, ILogger logger) {
            if(category is null || category.Id == 0) return null;

            var name = $"{contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(league).ToLower()}";
            if(guild.Channels.Any(c => c.Name == name)) return guild.Channels.First(c => c.Name == name);

            //Wait on the Server's lock, timeout defined in DiscordHostedService
            logger.LogInformation("CreateCoopThreadHeaderAsync: Waiting on Semaphore lock for guild {guild}", guild.Name);
            var dtNow = DateTimeOffset.Now;
            var ownershipAcquired = await guild.GetServerSemaphore().WaitAsync(DiscordHostedService.GetSemaphoreTimeout(), CancellationToken.None);
            if(ownershipAcquired) {
                logger.LogInformation("CreateCoopThreadHeaderAsync: Semaphore for guild {guild} unlocked after {timespan}.", guild.Name, TimeSpan.FromSeconds(DateTimeOffset.Now.ToUnixTimeSeconds() - dtNow.ToUnixTimeSeconds()).Humanize());
            } else {
                logger.LogInformation("CreateCoopThreadHeaderAsync: Semaphore for guild {guild} timed out after after {unlockTime} minutes.", guild.Name, DiscordHostedService.GetSemaphoreTimeout().TotalMinutes);
            }

            //Check again
            if(guild.Channels.Any(c => c.Name == name)) {
                if(ownershipAcquired) guild.GetServerSemaphore().Release();
                return guild.Channels.First(c => c.Name == name);
            }

            var channel = await guild.CreateTextChannelAsync(
                name,
                props => {
                    props.CategoryId = category.Id;
                    props.AutoArchiveDuration = ThreadArchiveDuration.OneDay;
                }
            );
            if(channel is null) {
                if(ownershipAcquired) guild.GetServerSemaphore().Release();
                return null;
            }
            await channel.SendMessageAsync(text: "", embed: contractEmbed);

            if(contract.cc_only && ultraRoles.Count > 0) {
                foreach(var ultraRole in ultraRoles) {
                    await channel.AddPermissionOverwriteAsync(ultraRole,
                        new OverwritePermissions(
                                viewChannel: PermValue.Allow,
                                sendMessages: PermValue.Deny,
                                sendMessagesInThreads: PermValue.Allow
                            )
                        );
                }
            } else if(leagueRole != null) {
                await channel.AddPermissionOverwriteAsync(leagueRole,
                    new OverwritePermissions(
                        viewChannel: PermValue.Allow,
                        sendMessages: PermValue.Deny,
                        sendMessagesInThreads: PermValue.Allow
                    )
                );
            }

            if(ownershipAcquired) guild.GetServerSemaphore().Release();
            return guild.GetChannel(channel.Id);
        }

        public static async Task DeleteCoopThreadHeadersAsync(this Guild guild, DiscordSocketClient client, Contract contract, ILogger logger) {
            List<SocketGuild> guilds = [
                client.GetGuild(guild.DiscordSeverId),
                .. guild.OverflowServers.Select(client.GetGuild).ToList()
            ];

            foreach(var sg in guilds) {
                var channels = sg.TextChannels.Where(c => c.Name.StartsWith(contract.GetE9KName().ToLower()) && Regex.IsMatch(c.Name, @"(-aaa|-aa|-a|-b|-c)$"));

                // Safety measure - there should never be more than 5 channels in the same guild,
                // so if this happens, the pattern matching failed.
                if(channels.Count() > 5) {
                    logger.LogError("Pattern matching failed for {guild} with the following channels: {channels} (They were not deleted)", sg.Name, String.Join(",", channels.Select(x => x.Name)));
                    continue;
                    
                }

                foreach(var channel in channels) {
                    await channel.DeleteAsync();
                }
            }
        }


        public static string GetE9KName(this Contract contract, bool toLower = true) {
            if(contract is null || string.IsNullOrEmpty(contract.Name) ) return "unknown-contract";
            return Regex.Replace((toLower ? contract.Name.ToLower() : contract.Name).Split(":").Last().Trim().Replace(" ", "-"), "[^a-zA-Z0-9-]", "");
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
