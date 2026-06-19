using Bugsnag;

using Discord;
using Discord.Net.Rest;
using Discord.Net.WebSockets;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class DiscordHostedService {
        public bool IsReady { get; private set; }
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IServiceProvider _provider;
        private readonly ILogger<DiscordHostedService> _logger;

        private readonly DiscordSocketClient _gateway;
        private readonly DiscordRestClient _rest;


        private static readonly DiscordSocketConfig config = new() {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.Guilds | GatewayIntents.GuildMessages |
                             GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessages | GatewayIntents.MessageContent,

        };
        private static readonly ConcurrentDictionary<ulong, SemaphoreSlim> _serverSemaphores = new();
        private static readonly TimeSpan _semaphoreTimeoutTime = TimeSpan.FromMinutes(1);

        // The gateway socket client, for use by CommandService and extension methods
        public DiscordSocketClient Gateway => _gateway;

        // Forwarding properties so _UpdaterBase and background jobs (_client.Guilds, _client.GetGuild etc.) work unchanged
        public IReadOnlyCollection<SocketGuild> Guilds => _gateway.Guilds;
        public SocketGuild GetGuild(ulong id) => _gateway.GetGuild(id);
        public SocketUser GetUser(ulong id) => _gateway.GetUser(id);
        public IChannel GetChannel(ulong id) => _gateway.GetChannel(id);
        public ConnectionState ConnectionState => _gateway.ConnectionState;
        public Task SendDMToKendrome(string message) => _gateway.SendDMToKendrome(message);

        // REST client, for callers that need guild/member lookups not available on the socket client
        public DiscordRestClient Rest => _rest;

        // Application emotes - used by NewContracts.cs and FAQCommandSlash.cs
        public Task<IReadOnlyCollection<Emote>> GetApplicationEmotesAsync() => _gateway.GetApplicationEmotesAsync();

        // Async channel lookup by raw ID - used by ContractUpdater.cs and CreateCoopThreads.cs
        public Task<IChannel> GetChannelAsync(ulong id) => (_gateway as IDiscordClient).GetChannelAsync(id);

        public DiscordHostedService(Microsoft.Extensions.Configuration.IConfiguration Configuration, IMemoryCache cache, IServiceProvider provider, ILogger<DiscordHostedService> logger) {
#if DEBUG
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
#endif

            _configuration = Configuration;
            _provider = provider;
            _logger = logger;

            _gateway = new DiscordSocketClient(config);
            _rest = new DiscordRestClient();

            _gateway.Log += PrintLog;
            _gateway.Ready += DiscordHostedService_Ready;
            _gateway.LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();
            _gateway.StartAsync().Wait();
            _rest.LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();

            _logger.Log(LogLevel.Information, "Waiting on Discord Connect");
            while(_gateway.ConnectionState != ConnectionState.Connected) { }
            _logger.Log(LogLevel.Information, "Discord Ready");

            _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            _cache = cache;

            foreach(var guild in _gateway.Guilds) {
                _serverSemaphores.TryAdd(guild.Id, new SemaphoreSlim(1, 1));
            }
        }

        public class RestartDiscordException(string customMessage, Severity severity) : Exception(customMessage) {
            public string CustomMessage { get; set; } = customMessage;
            public Severity Severity { get; set; } = severity;
        }

        public async Task RestartAsync() {
            if(_gateway.ConnectionState != ConnectionState.Connected) {
                throw new RestartDiscordException("Not connected yet - cannot restart.", Severity.Warning);
            }

            try {
                //Logout subtasks
                await _gateway.LogoutAsync();
                await _gateway.StopAsync();
                _logger.Log(LogLevel.Information, "Waiting on Discord Disconnect");
                while(_gateway.ConnectionState == ConnectionState.Connected) { }
                _logger.Log(LogLevel.Information, "Discord Disconnected...");

                //Log back in
                await _gateway.LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]);
                await _gateway.StartAsync();
                _logger.Log(LogLevel.Information, "Waiting on Discord Connect");
                while(_gateway.ConnectionState != ConnectionState.Connected) { }
                _logger.Log(LogLevel.Information, "Discord Ready");
            } catch(Exception ex) {
                throw new RestartDiscordException(ex.Message, Severity.Error);
            }

            return;
        }


        private Task DiscordHostedService_Ready() {
            IsReady = true;
            _gateway.SetGameAsync("").Wait();

            foreach(var guild in _gateway.Guilds) {
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

                return (T)Convert.ChangeType(_gateway.GetChannel(channelDetail.Id), typeof(T));
            } catch(Exception e) {
                _logger.LogError(e, "Error getting channel or category");
                return default;
            }
        }
        public static SemaphoreSlim GetOrCreateSemaphore(SocketGuild guild) {
            return _serverSemaphores.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
        }
        public static TimeSpan GetSemaphoreTimeout() {
            return _semaphoreTimeoutTime;
        }


    }



    public static class DiscordExtensions {
        public static async Task SendDMToKendrome(this DiscordSocketClient _discord, string message) {
            var kendromeUser = _discord.GetUser(248865520756064257);
            if(kendromeUser is null) return;
            var kendromedmchannel = await kendromeUser.CreateDMChannelAsync();
            if(kendromedmchannel is not null) {
                await kendromedmchannel.SendMessageAsync(message);
            }
        }
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
            var dtNow = DateTimeOffset.UtcNow;
            var ownershipAcquired = await guild.GetServerSemaphore().WaitAsync(DiscordHostedService.GetSemaphoreTimeout(), CancellationToken.None);
            if(ownershipAcquired) {
                logger.LogInformation("CreateCoopThreadHeaderAsync: Semaphore for guild {guild} unlocked after {timespan}.", guild.Name, TimeSpan.FromSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - dtNow.ToUnixTimeSeconds()).Humanize());
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
                    props.Topic = contract.ID;
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

        private readonly static MemoryCache commandCache = new(new MemoryCacheOptions());

        private static string GetCommandCacheKey(this SocketGuild guild) { return $"CommandCache{guild.Id}"; }

        public static async Task<IReadOnlyCollection<SocketApplicationCommand>> GetCachedApplicationCommands(this SocketGuild guild) {
            if(!commandCache.TryGetValue(guild.GetCommandCacheKey(), out IReadOnlyCollection<SocketApplicationCommand> commands)) {
                commands = await guild.GetApplicationCommandsAsync();
                commandCache.Set(guild.GetCommandCacheKey(), commands, TimeSpan.FromMinutes(10));
            }
            return commands;
        }

        public static async Task<IReadOnlyCollection<SocketApplicationCommand>> GetCachedApplicationCommands(this DiscordHostedService discord) {
            if(!commandCache.TryGetValue("GLOBAL", out IReadOnlyCollection<SocketApplicationCommand> commands)) {
                commands = await discord.Gateway.GetGlobalApplicationCommandsAsync();
                commandCache.Set("GLOBAL", commands, TimeSpan.FromMinutes(10));
            }
            return commands;
        }

        public static async Task<string> GetSlashCommandStringAsync(this DiscordHostedService discord, SocketGuild guild, string slashCommandName) {
            var fixedSlashCommandName = slashCommandName.ToLower().Trim();
            var command = (await guild.GetCachedApplicationCommands())
                .ToList().Where(c => c.Type == ApplicationCommandType.Slash)
                .FirstOrDefault(c => c.Name.ToLower() == fixedSlashCommandName);
            command ??= (await discord.GetCachedApplicationCommands())
                .ToList().Where(c => c.Type == ApplicationCommandType.Slash)
                .FirstOrDefault(c => c.Name.ToLower() == fixedSlashCommandName);

            if(command == null) return $"`/{fixedSlashCommandName}`";
            else return $"</{fixedSlashCommandName}:{command.Id}>";
        }

        public static string GetE9KName(this Contract contract, bool toLower = true) {
            if(contract is null || string.IsNullOrEmpty(contract.Name)) return "unknown-contract";
            return Regex.Replace((toLower ? contract.Name.ToLower() : contract.Name).Split(":").Last().Trim().Replace(" ", "-"), "[^a-zA-Z0-9-]", "");
        }

        public static async Task<SocketTextChannel> GetParentChannelAsync(this IThreadChannel threadChannel) {
            try {
                return (await threadChannel.Guild.GetTextChannelAsync(threadChannel.CategoryId ?? ulong.MaxValue)) as SocketTextChannel ?? null;
            } catch(Exception) {
                return null;
            }
        }

#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        public static async Task<Emote> CreateCustomEggEmoji(this DiscordHostedService _client, Ei.CustomEgg newEgg, Emote? emoteToReplace) {
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
            var emojiName = newEgg.GetEmojiName();
            var existingEmotes = await _client.Gateway.GetApplicationEmotesAsync();
            // Download the image from aux
            var imageUrl = newEgg.Icon.Url.ToString();
            byte[] imageBytes;
            using var _httpClient = new HttpClient();
            using var response = await _httpClient.GetAsync(imageUrl, CancellationToken.None);
            response.EnsureSuccessStatusCode();
            imageBytes = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);

            // Check if the image is larger than 256KB, if so scale it down
            // Because of file headers, etc. we aim to mutate down to 200KB
            // If that is STILL too big, repeatedly scale by 0.9x until the file is small enough
            const int maxSizeInBytes = 200 * 1024;
            const double scaleFactorStep = 0.9;

            while(imageBytes.Length > maxSizeInBytes) {
                using var image = SixLabors.ImageSharp.Image.Load(imageBytes);

                // Calculate the new size to maintain aspect ratio
                var scaleFactor = Math.Sqrt((double)maxSizeInBytes / imageBytes.Length) * scaleFactorStep;
                var newWidth = (int)(image.Width * scaleFactor);
                var newHeight = (int)(image.Height * scaleFactor);

                // Resize the image
                image.Mutate(x => x.Resize(newWidth, newHeight));

                // Save the resized image to a byte array
                using var ms = new MemoryStream();
                image.Save(ms, new PngEncoder());
                imageBytes = ms.ToArray();
            }

            // Convert the image to a stream, then to a Discord Image
            using var imageStream = new MemoryStream(imageBytes);
            var discordImage = new Image(imageStream);

            // Upload the image as a GuildEmote
            var newAppEmote = await _client.Gateway.CreateApplicationEmoteAsync(emojiName, discordImage);

            if(emoteToReplace != null && newAppEmote != null) {
                var appEmote = await _client.Gateway.GetApplicationEmoteAsync(emoteToReplace.Id);
                if(appEmote is not null) {
                    await _client.Gateway.DeleteApplicationEmoteAsync(emoteToReplace.Id, options: new RequestOptions() {
                        RetryMode = RetryMode.RetryRatelimit | RetryMode.RetryTimeouts
                    });
                }
            }

            return newAppEmote ?? emoteToReplace ?? null;
        }
    }
}
