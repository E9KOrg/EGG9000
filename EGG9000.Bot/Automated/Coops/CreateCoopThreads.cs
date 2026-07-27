using Discord;
using Discord.Net;
using Discord.WebSocket;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Humanizer;
using MassTransit.Initializers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.CreateCoopsV2;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public class CreateCoopThreads(IServiceProvider provider, ThreadsCoopStatusUpdater threadsCoopStatusUpdater, BotLogger botLogger, CoopMessageSender coopMessageSender) : _UpdaterBase<CreateCoopThreads>(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(0), provider) {
        private readonly ThreadsCoopStatusUpdater _threadsCoopStatusUpdater = threadsCoopStatusUpdater;
        private readonly BotLogger _botLogger = botLogger;
        private readonly CoopMessageSender _coopMessageSender = coopMessageSender;
        private static readonly TimeSpan DefaultThreadCreationDelay = TimeSpan.FromMilliseconds(6050);

        private readonly Dictionary<string, (int Count, DateTimeOffset LastSeen)> CoopsTimeoutCounter = [];
        private static readonly TimeSpan CoopsTimeoutCounterTtl = TimeSpan.FromMinutes(10);

        public async override Task Run(object state, CancellationToken cancellationToken) {
            ulong.TryParse(_configuration.GetConnectionString("CPGuildId"), out var _CPGuildId);

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();


            List<Coop> allCoops;
            var throttler = new SemaphoreSlim(5);
            var tasks = new List<Task>();

            var dbguilds = _db.CachedGuilds.ToList();

            Dictionary<(ulong guildid, string contractid, ulong bggroup), (int successes, int failures, bool changed)> guildStats = [];

            while(
                (allCoops = await _db.Coops.Include(c => c.Contract).AsQueryable().Where(x => x.Status == CoopStatusEnum.WaitingOnThread).OrderByDescending(x => x.MaxUsers).ToListAsync(CancellationToken.None))
                .Count > 0) {
                if(cancellationToken.IsCancellationRequested) return;

                var guildIDs = allCoops.Select(x => x.GuildId).Distinct().ToList();


                var coops = new List<Coop>();

                while(allCoops.Count > 0 && coops.Count < 20) {
                    foreach(var guildID in guildIDs) {
                        if(allCoops.Any(x => x.GuildId == guildID && x.League < (uint)Ei.Contract.Types.PlayerGrade.GradeAaa)) {
                            var lowgradeCoop = allCoops.FirstOrDefault(x => x.GuildId == guildID && x.League < (uint)Ei.Contract.Types.PlayerGrade.GradeAaa);
                            if(lowgradeCoop != null) {
                                coops.Add(lowgradeCoop);
                                allCoops.Remove(lowgradeCoop);
                            }
                        }
                        var coop = allCoops.FirstOrDefault(x => x.GuildId == guildID);
                        if(coop != null) {
                            coops.Add(coop);
                            allCoops.Remove(coop);
                        }
                    }
                }



                var contractIDs = coops.Select(x => x.ContractID).Distinct().ToList();
                var guildContracts = await _db.GuildContracts.Include(gc => gc.Contract).Where(gc => guildIDs.Contains(gc.GuildID) && contractIDs.Contains(gc.ContractID)).ToListAsync(cancellationToken);

                var guildsWithOverflow = new List<(SocketGuild Guild, List<OverflowServer> Servers, DateTimeOffset LastAccessed)>();

                foreach(var guild in _client.Guilds.Where(x => coops.Any(y => y.GuildId == x.Id)).OrderBy(x => x.Id == _CPGuildId)) {
                    var dbguild = dbguilds.FirstOrDefault(x => x.Id == guild.Id);
                    if(dbguild != null) {
                        guildsWithOverflow.Add((guild, await GetOverflowGuildsCounts(guild, dbguild), DateTimeOffset.MinValue));
                    }
                }

                var headerChannels = await GetOrCreateHeaderChannelsForCoops(_db, coops, dbguilds, guildContracts);


                if(coops.Count > 5) {
                    _coopsBeingCreatedService.SetCoopThreadsAreBeingCreated(true);
                }
                foreach(var coop in coops) {
                    var timings = new TimingsFactory(_logger);
                    timings.Start();

                    StillAlive();
                    if(coop.ContractID is null) {
                        PruneCoopsTimeoutCounter();
                        if(CoopsTimeoutCounter.TryGetValue(coop.Name, out var timeoutEntry)) {
                            if(timeoutEntry.Count > 60) {
                                _logger.LogWarning("Unable to create channel for coop {coop} because the contract is null", coop.Name);
                                CoopsTimeoutCounter[coop.Name] = (0, DateTimeOffset.UtcNow);
                            } else {
                                CoopsTimeoutCounter[coop.Name] = (timeoutEntry.Count + 1, DateTimeOffset.UtcNow);
                                if(allCoops.All(x => CoopsTimeoutCounter.ContainsKey(x.Name))) {
                                    goto ExitWhile;
                                }
                            }
                        } else {
                            _logger.LogWarning("Unable to create channel for coop {coop} because the contract is null", coop.Name);
                            CoopsTimeoutCounter.Add(coop.Name, (1, DateTimeOffset.UtcNow));
                        }
                        continue;
                    }
                    if(cancellationToken.IsCancellationRequested) return;
                    var guildWithOverflow = guildsWithOverflow.First(x => x.Guild.Id == coop.GuildId);

                    try {
                        var guildContract = guildContracts.First(gc => gc.GuildID == guildWithOverflow.Guild.Id && string.Equals(gc.ContractID, coop.ContractID, StringComparison.CurrentCultureIgnoreCase));
                        var secondsRemaining = Math.Max(guildContract.Contract.Details.LengthSeconds, TimeSpan.FromDays(1.6).TotalSeconds);

                        timings.Set("Setup");


                        var headerChannel = await GetHeaderChannelAndWait(headerChannels, coop);
                        var headerChannelInfo = headerChannels.First(x => x.GuildId == coop.GuildId).HeaderChannels.FirstOrDefault(x => x.HeaderChannel?.Id == headerChannel?.Id);
                        timings.Set("Got Header Channel");
                        if(headerChannel == null) {
                            _logger.LogError("Unable to get header channel for {coop} in contract {contract}", coop.Name, guildContract.ContractID);
                        } else {
                            if(coop.ThreadID > 0) {
                                var existingThread = (SocketThreadChannel)await _client.GetChannelAsync(coop.ThreadID);
                                _logger.LogWarning("Trying to create a new thread for {coop} already has a thread at {thread}", coop.Name, existingThread?.Name ?? "null");
                                continue;
                            }
                            var coopThread = await _queue.EnqueueLowAsync(() => CreateThreadChannelAsync(coop.Name, headerChannel));
                            if(coopThread != null) {
                                timings.Set("Thread created");
                                coop.Status = CoopStatusEnum.WaitingOnAssigned;
                                coop.ThreadID = coopThread.Id;
                                coop.ThreadParentChannel = headerChannel.Id;
                                coop.OverflowGuildId = headerChannel.Guild.Id;
                                _logger.LogInformation("Thread created for {coopName} in {guild}", coop.Name, headerChannel.Guild.Name);
                                using var writeScope = _provider.CreateScope();
                                var writeDb = writeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                await writeDb.Coops.Where(c => c.Id == coop.Id).ExecuteUpdateAsync(s => s
                                    .SetProperty(c => c.Status, coop.Status)
                                    .SetProperty(c => c.ThreadID, coop.ThreadID)
                                    .SetProperty(c => c.ThreadParentChannel, coop.ThreadParentChannel)
                                    .SetProperty(c => c.OverflowGuildId, coop.OverflowGuildId));
                                timings.Set("Updated db");

                                guildWithOverflow.LastAccessed = DateTimeOffset.UtcNow;
                                var dbguild = dbguilds.FirstOrDefault(x => x.Id == guildWithOverflow.Guild.Id);
                                var overflowGuild = coop.OverflowGuildId > 0 ? _client.GetGuild(coop.OverflowGuildId) : guildWithOverflow.Guild;




                                while(!await throttler.WaitAsync(20000, cancellationToken)) {
                                    _logger.LogInformation("Waiting on throttle");
                                }

                                timings.Set("Waited Throttle");


                                tasks.Add(Task.Run(async () => {
                                    try {
                                        var db2 = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                        var xrefs = await db2.UserCoopXrefs.Include(x => x.User).AsQueryable().Where(x => x.CoopId == coop.Id).ToListAsync();
                                        var coopToUpdate = await db2.Coops.FirstAsync(c => c.Id == coop.Id);

                                        var capturedThread = coopThread;
                                        var capturedWebhook = headerChannelInfo?.Webhook;
                                        var introText = $"Coop **{coop.Name}** for the contract **{guildContract.Contract.Name}** is ready for the following to join: {string.Join(", ", xrefs.Select(x => $"<@{x.User.DiscordId}>" + (x.User.EggIncAccounts.Count > 1 ? $"({x.User.EggIncAccounts.FirstOrDefault(e => e.Id == x.EggIncId)?.Backup.UserName ?? "Check website"})" : "")))}\n";

                                        var trackedMessages = new List<TrackedMessage> {
                                            await _coopMessageSender.SendAsync(capturedThread, capturedWebhook, content: introText),
                                            await _coopMessageSender.SendAsync(capturedThread, capturedWebhook, content: "\u17B5"),
                                            await _coopMessageSender.SendAsync(capturedThread, capturedWebhook, content: "\u17B5"),
                                            await _coopMessageSender.SendAsync(capturedThread, capturedWebhook, content: "\u17B5"),
                                            await _coopMessageSender.SendAsync(capturedThread, capturedWebhook, content: "\u17B5")
                                        };
                                        coopToUpdate.UpdateMessagesId = TrackedMessageSerializer.Serialize(trackedMessages);
                                        await db2.SaveChangesAsyncRetry(cancellationToken: cancellationToken);
                                    } finally {
                                        throttler.Release();
                                    }
                                }, cancellationToken));

                                IncrementGuildStats(guildStats, coop.GuildId, guildContract.Contract.ID, coop.Group, true);
                                var timingsReulsts = timings.Finished();

                                _logger.LogInformation("Timings for creating thread for {coopName} in {guild}: {timings}", coop.Name, headerChannel.Guild.Name, string.Join(", ", timingsReulsts.Select(x => $"{x.name}: {x.time.TotalSeconds}s")));
                            } else {

                                IncrementGuildStats(guildStats, coop.GuildId, guildContract.Contract.ID, coop.Group, false);
                                _logger.LogWarning("Thread NOT created for {coopName} in {guild}", coop.Name, guildWithOverflow.Guild.Name);
                            }
                        }
                    } catch(Exception ex) {
                        _logger.LogError(ex, "Error Creating Co-op Thread {coop} in {guild}", coop.Name, guildWithOverflow.Guild.Name);

                    }
                }

                foreach(var guildStat in guildStats.Where(x => x.Value.changed)) {
                    guildStats[guildStat.Key] = (guildStat.Value.successes, guildStat.Value.failures, false);
                    await _botLogger.RefreshBoardingGroup((int)guildStat.Key.bggroup, guildStat.Key.contractid, guildStat.Key.guildid);
                }

            }
            _coopsBeingCreatedService.SetCoopThreadsAreBeingCreated(false);
        ExitWhile:

            if(tasks.Count > 0) {
                var watchdogCancellationSource = new CancellationTokenSource();
                var watchdogCancelToken = watchdogCancellationSource.Token;
                var watchdogTask = Task.Delay(TimeSpan.FromMinutes(10), watchdogCancelToken);
                var allTasks = Task.WhenAll(tasks);
                var completedTask = await Task.WhenAny(allTasks, watchdogTask);

                if(completedTask == watchdogTask) {
                    watchdogCancellationSource.Cancel();
                    _logger.LogWarning("Watchdog Task Called, not all tasks finished. (Completed {count} of {total})", tasks.Count(x => x.IsCompleted), tasks.Count);
                }
                _logger.LogInformation("Finished created {count} co-ops", tasks.Count);
            }
        }


        private void IncrementGuildStats(Dictionary<(ulong guildid, string contractid, ulong bggroup), (int successes, int failures, bool changed)> guildStats, ulong guildId, string contractId, ulong bgGroup, bool success) {
            lock(guildStats) {
                var key = (guildId, contractId, bgGroup);
                if(!guildStats.ContainsKey(key)) {
                    guildStats[key] = (0, 0, false);
                }
                if(success) {
                    guildStats[key] = (guildStats[key].successes + 1, guildStats[key].failures, true);
                } else {
                    guildStats[key] = (guildStats[key].successes, guildStats[key].failures + 1, true);
                }
            }
        }

        private void PruneCoopsTimeoutCounter() {
            var cutoff = DateTimeOffset.UtcNow - CoopsTimeoutCounterTtl;
            foreach(var key in CoopsTimeoutCounter.Where(x => x.Value.LastSeen < cutoff).Select(x => x.Key).ToList()) {
                CoopsTimeoutCounter.Remove(key);
            }
        }

        private async Task<IThreadChannel> CreateThreadChannelAsync(string threadName, SocketGuildChannel parentChannel) {
            try {
                var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(1800));
                var thread = await (parentChannel as SocketTextChannel).CreateThreadAsync(
                    name: threadName,
                    type: ThreadType.PrivateThread,
                    autoArchiveDuration: ThreadArchiveDuration.OneWeek, //Initially one week (don't archive)
                    invitable: false,
                    options: new RequestOptions {
                        RatelimitCallback = (IRateLimitInfo info) => RateLimit(info, threadName, parentChannel.Id), CancelToken = cts.Token
                    }
                );
                cts.Dispose();
                return thread;
            } catch(HttpException dException) {
                if(dException.DiscordCode == DiscordErrorCode.MaximumActiveThreadsReached) {
                }
            } catch(Exception e) {
                _logger.LogWarning(e, "Failed to create thread {thread}", threadName);
            }
            return null;
        }

        private Task RateLimit(IRateLimitInfo info, string threadName, ulong channelId) {
            lock(__headerChannelLock) {
                _lastRateLimitInfoByChannel[channelId] = (info, DateTimeOffset.UtcNow);
                var cutoff = DateTimeOffset.UtcNow - RateLimitInfoTtl;
                foreach(var key in _lastRateLimitInfoByChannel.Where(x => x.Value.LastSeen < cutoff).Select(x => x.Key).ToList()) {
                    _lastRateLimitInfoByChannel.Remove(key);
                }
            }
            _logger.LogWarning("Rate Limit for {thread}- Limit:{Limit} Remaining:{Remaining} RetryAfter:{RetryAfter} Reset:{Reset} ResetAfter:{After}",
                               threadName,
                               info.Limit,
                               info.Remaining,
                               TimeSpan.FromSeconds(info.RetryAfter ?? 0).Humanize(precision: 2).ShortenTime(),
                               info.Reset?.Humanize().ShortenTime(),
                               info.ResetAfter?.Humanize(precision: 2).ShortenTime()
                               );
            return Task.CompletedTask;
        }

        public class HeaderChannelsForGuild {
            public ulong GuildId { get; set; }
            public List<ServerHeaderChannel> HeaderChannels = [];
            public List<LastAccessedByServer> LastAccessed = [];
        }

        public class ServerHeaderChannel {
            public uint League { get; set; }
            public string ContractId { get; set; }
            public ulong ServerId { get; set; }
            public int ChannelIndex { get; set; }
            public SocketGuildChannel HeaderChannel { get; set; }
            public HeaderChannelWebhook Webhook { get; set; }
        }

        public class LastAccessedByServer {
            public ulong ServerId { get; set; }
            public int ChannelIndex { get; set; }
            public DateTimeOffset LastAccessed { get; set; }
        }

        private readonly object __headerChannelLock = new();
        private readonly Dictionary<ulong, (IRateLimitInfo Info, DateTimeOffset LastSeen)> _lastRateLimitInfoByChannel = [];
        private static readonly TimeSpan RateLimitInfoTtl = TimeSpan.FromMinutes(10);

        private async Task<SocketGuildChannel> GetHeaderChannelAndWait(List<HeaderChannelsForGuild> headerChannels, Coop coop) {
            SocketGuildChannel headerChannel;
            DateTimeOffset lastAccessed;
            lock(__headerChannelLock) {
                var headerChannelsForGuild = headerChannels.First(x => x.GuildId == coop.GuildId);
                var currentChannels = headerChannelsForGuild.HeaderChannels.Where(x => x.League == coop.League && x.ContractId == coop.ContractID).ToList();
                var lastAccessedObject = headerChannelsForGuild.LastAccessed.Where(la => currentChannels.Any(x => x.ServerId == la.ServerId && x.ChannelIndex == la.ChannelIndex)).OrderBy(la => la.LastAccessed).First();
                var serverHeaderChannel = currentChannels.First(x => x.ServerId == lastAccessedObject.ServerId && x.ChannelIndex == lastAccessedObject.ChannelIndex);
                headerChannel = serverHeaderChannel.HeaderChannel;
                lastAccessed = lastAccessedObject.LastAccessed;
                lastAccessedObject.LastAccessed = DateTimeOffset.UtcNow;
            }

            var nextAllowed = CalculateNextAllowedTime(headerChannel?.Id ?? 0, lastAccessed);
            if(nextAllowed > DateTimeOffset.UtcNow) {
                var timeToDelay = nextAllowed - DateTimeOffset.UtcNow;
                _logger.LogInformation("Delaying for {delay} on {guild}", timeToDelay.Humanize(precision: 2).ShortenTime(), headerChannel?.Guild?.Name);
                await Task.Delay(timeToDelay);
            }
            return headerChannel;
        }

        internal DateTimeOffset CalculateNextAllowedTime(ulong channelId, DateTimeOffset lastAccessed) {
            IRateLimitInfo info;
            lock(__headerChannelLock) {
                info = _lastRateLimitInfoByChannel.TryGetValue(channelId, out var entry) ? entry.Info : null;
            }

            // No response seen yet for this channel - use the fixed default so the first
            // thread-create in a channel doesn't fire immediately and guarantee a 429.
            if(info == null || info.Remaining == null || info.Reset == null) {
                return lastAccessed + DefaultThreadCreationDelay;
            }

            // Bucket has capacity remaining - no need to wait at all.
            if(info.Remaining > 0) {
                return DateTimeOffset.UtcNow;
            }

            // Bucket exhausted - wait until Discord's own reset time for this channel's bucket.
            return info.Reset.Value;
        }

        private async Task<List<HeaderChannelsForGuild>> GetOrCreateHeaderChannelsForCoops(ApplicationDbContext db, List<Coop> coops, List<Guild> guilds, List<GuildContract> guildContracts) {
            List<HeaderChannelsForGuild> headerChannelsForGuilds = [];
            foreach(var guild in guilds) {
                HeaderChannelsForGuild headerChannelsForGuild = new HeaderChannelsForGuild { GuildId = guild.Id };
                headerChannelsForGuilds.Add(headerChannelsForGuild);
                headerChannelsForGuild.LastAccessed.Add(new LastAccessedByServer { ServerId = guild.Id, ChannelIndex = 0, LastAccessed = DateTimeOffset.MinValue });
                foreach(var overflowServerId in guild.OverflowServers) {
                    headerChannelsForGuild.LastAccessed.Add(new LastAccessedByServer { ServerId = overflowServerId, ChannelIndex = 0, LastAccessed = DateTimeOffset.MinValue });
                    headerChannelsForGuild.LastAccessed.Add(new LastAccessedByServer { ServerId = overflowServerId, ChannelIndex = 1, LastAccessed = DateTimeOffset.MinValue });
                }

                var contractGroups = coops.Where(x => x.GuildId == guild.Id).GroupBy(x => new { x.ContractID, x.GuildId, x.League });
                foreach(var contractGroup in contractGroups) {
                    var mainServer = _client.Guilds.First(x => x.Id == guild.Id);
                    var guildContract = guildContracts.First(x => x.GuildID == guild.Id && x.ContractID == contractGroup.Key.ContractID);
                    if(guild.OverflowServers.Any() && PlayerGradeDetails.GetGradeFromLeague(contractGroup.Key.League) == Ei.Contract.Types.PlayerGrade.GradeAaa) {
                        const int ChannelsPerOverflowServer = 2;

                        if(contractGroup.Count() > 2) {
                            foreach(var overflow in guild.OverflowServers) {
                                var overflowServer = _client.Guilds.First(x => x.Id == overflow);

                                for(var channelIndex = 0; channelIndex < ChannelsPerOverflowServer; channelIndex++) {
                                    var (headerChannel, webhook) = await GetOrCreateHeaderChannel(db, contractGroup.Key.League, overflowServer, mainServer, guildContract, channelIndex);
                                    headerChannelsForGuild.HeaderChannels.Add(new ServerHeaderChannel { ContractId = contractGroup.Key.ContractID, HeaderChannel = headerChannel, ServerId = overflowServer.Id, ChannelIndex = channelIndex, League = contractGroup.Key.League, Webhook = webhook });
                                }
                            }
                        } else {
                            var overflowServer = _client.Guilds.First(x => x.Id == guild.OverflowServers.First());
                            var (headerChannel, webhook) = await GetOrCreateHeaderChannel(db, contractGroup.Key.League, overflowServer, mainServer, guildContract, 0);
                            headerChannelsForGuild.HeaderChannels.Add(new ServerHeaderChannel { ContractId = contractGroup.Key.ContractID, HeaderChannel = headerChannel, ServerId = overflowServer.Id, League = contractGroup.Key.League, Webhook = webhook });

                        }
                    } else {
                        var (headerChannel, webhook) = await GetOrCreateHeaderChannel(db, contractGroup.Key.League, mainServer, mainServer, guildContract, 0);
                        headerChannelsForGuild.HeaderChannels.Add(new ServerHeaderChannel { ContractId = contractGroup.Key.ContractID, HeaderChannel = headerChannel, ServerId = mainServer.Id, League = contractGroup.Key.League, Webhook = webhook });
                    }
                }
            }
            return headerChannelsForGuilds;
        }


        private async Task<(SocketGuildChannel Channel, HeaderChannelWebhook Webhook)> GetOrCreateHeaderChannel(ApplicationDbContext db, uint League, SocketGuild OverflowSocketGuild, SocketGuild MainSocketGuild, GuildContract GuildContract, int channelIndex) {
            var channelName = GuildContract.Contract.GetHeaderChannelName(League, channelIndex);

            var existingRow = await db.CoopHeaderChannels.FirstOrDefaultAsync(x =>
                x.GuildId == MainSocketGuild.Id && x.ContractID == GuildContract.ContractID && x.League == League &&
                x.ServerId == OverflowSocketGuild.Id && x.ChannelIndex == channelIndex);

            if(existingRow != null) {
                var existingChannel = OverflowSocketGuild.GetChannel(existingRow.ChannelId);
                if(existingChannel != null) {
                    return (existingChannel, new HeaderChannelWebhook { WebhookId = existingRow.WebhookId, WebhookToken = existingRow.WebhookToken });
                }
                // Channel was deleted out-of-band; fall through and recreate.
            }

            var headerChannel = OverflowSocketGuild.Channels.FirstOrDefault(c => c.Name == channelName);
            HeaderChannelWebhook webhook;

            if(headerChannel != null) {
                webhook = await GetOrCreateWebhookForChannel((SocketTextChannel)headerChannel);
            } else {
                headerChannel = await CreateHeaderChannelInternal(db, League, OverflowSocketGuild, MainSocketGuild, GuildContract, channelIndex);
                if(headerChannel == null) return (null, null);
                webhook = await GetOrCreateWebhookForChannel((SocketTextChannel)headerChannel);
            }

            if(existingRow != null) {
                // Row already tracked by this context - update in place instead of Add()ing a
                // new entity with the same composite PK (would throw InvalidOperationException).
                _logger.LogWarning("Header channel row for {guild}/{contract}/{league}/{server}/{index} is being recreated; overwriting WebhookId/WebhookToken. Coop messages still tracked against the old webhook will self-heal on their next edit.", MainSocketGuild.Id, GuildContract.ContractID, League, OverflowSocketGuild.Id, channelIndex);
                existingRow.ChannelId = headerChannel.Id;
                existingRow.WebhookId = webhook.WebhookId;
                existingRow.WebhookToken = webhook.WebhookToken;
                existingRow.Created = DateTimeOffset.UtcNow;
            } else {
                var row = new CoopHeaderChannel {
                    GuildId = MainSocketGuild.Id, ContractID = GuildContract.ContractID, League = League,
                    ServerId = OverflowSocketGuild.Id, ChannelIndex = channelIndex,
                    ChannelId = headerChannel.Id, WebhookId = webhook.WebhookId, WebhookToken = webhook.WebhookToken,
                    Created = DateTimeOffset.UtcNow
                };
                db.CoopHeaderChannels.Add(row);
            }
            await db.SaveChangesAsyncRetry();

            return (headerChannel, webhook);
        }

        private async Task<HeaderChannelWebhook> GetOrCreateWebhookForChannel(SocketTextChannel channel) {
            return await _queue.EnqueueLowAsync(async () => {
                // No separate webhook branding - name/avatar match the bot's own profile so
                // webhook-lane messages look indistinguishable from bot-lane ones.
                var botUser = _client.Gateway.CurrentUser;
                var webhookName = botUser.Username;
                var existing = (await channel.GetWebhooksAsync()).FirstOrDefault(w => w.Name == webhookName);
                if(existing != null) {
                    return new HeaderChannelWebhook { WebhookId = existing.Id, WebhookToken = existing.Token };
                }
                using var avatarStream = await DownloadBotAvatarAsync(botUser);
                var webhook = await channel.CreateWebhookAsync(webhookName, avatarStream);
                return new HeaderChannelWebhook { WebhookId = webhook.Id, WebhookToken = webhook.Token };
            });
        }

        private async Task<Stream> DownloadBotAvatarAsync(SocketSelfUser botUser) {
            var avatarUrl = botUser.GetAvatarUrl(size: 256) ?? botUser.GetDefaultAvatarUrl();
            using var httpClient = new HttpClient();
            var bytes = await httpClient.GetByteArrayAsync(avatarUrl);
            return new MemoryStream(bytes);
        }

        private async Task<SocketGuildChannel> CreateHeaderChannelInternal(ApplicationDbContext db, uint League, SocketGuild OverflowSocketGuild, SocketGuild MainSocketGuild, GuildContract GuildContract, int channelIndex) {
            var channelName = GuildContract.Contract.GetHeaderChannelName(League, channelIndex);
            var gradeRoleEnum = League switch {
                5 => GuildChannelType.GradeAAA,
                4 => GuildChannelType.GradeAA,
                3 => GuildChannelType.GradeA,
                2 => GuildChannelType.GradeB,
                1 => GuildChannelType.GradeC,
                _ => GuildChannelType.General,
            };
            SocketRole gradeRole = null;
            if(gradeRoleEnum != GuildChannelType.General) {
                gradeRole = await _client.GetRoleAsync(gradeRoleEnum, MainSocketGuild);
                if(gradeRole != null && MainSocketGuild.Id != OverflowSocketGuild.Id) {
                    gradeRole = OverflowSocketGuild.Roles.FirstOrDefault(r => r.Name == gradeRole.Name);
                }
            }

            List<SocketRole> ultraRoles = [];
            if(GuildContract.Contract.Details.CcOnly) {
                var ultraStandardRole = await _client.GetRoleAsync(GuildChannelType.StandardSubscription, MainSocketGuild);
                if(ultraStandardRole != null && MainSocketGuild.Id != OverflowSocketGuild.Id) {
                    ultraStandardRole = OverflowSocketGuild.Roles.FirstOrDefault(r => r.Name == ultraStandardRole.Name);
                }
                if(ultraStandardRole != null) ultraRoles.Add(ultraStandardRole);

                var ultraProRole = await _client.GetRoleAsync(GuildChannelType.ProSubscription, MainSocketGuild);
                if(ultraProRole != null && MainSocketGuild.Id != OverflowSocketGuild.Id) {
                    ultraProRole = OverflowSocketGuild.Roles.FirstOrDefault(r => r.Name == ultraProRole.Name);
                }
                if(ultraProRole != null) ultraRoles.Add(ultraProRole);
            }

            _logger.LogInformation("Creating header channel {name} for {contract} {grade} in {server}", channelName, GuildContract.Contract.GetE9KName(), PlayerGradeDetails.GetNameFromLeague(League), OverflowSocketGuild.Name);

            var contractEmbed = await ContractUpdater.GetContractEmbed(GuildContract, db, MainSocketGuild, (Ei.Contract.Types.PlayerGrade)League);
            var categories = (await _client.GetAllCoopCategories(OverflowSocketGuild))?.Select(x => new CoopCategories(OverflowSocketGuild, x)).ToList() ?? [];
            var category = categories?.OrderBy(x => x.DiscordCategory.Position)?.FirstOrDefault(x => x.CurrentCount < 50);

            if(BuildConfig.IsDev9002 && category == null) {
                var newCategory = await OverflowSocketGuild.CreateCategoryChannelAsync("Coops");
                categories = (await _client.GetAllCoopCategories(OverflowSocketGuild)).Select(x => new CoopCategories(OverflowSocketGuild, x)).ToList();
                category = categories.OrderBy(x => x.DiscordCategory.Position).FirstOrDefault(x => x.CurrentCount < 50);
            }
            if(category == null) {
                _logger.LogError("No coop category with available space found in {server} for {contract} grade {grade}", OverflowSocketGuild.Name, GuildContract.Contract.GetE9KName(), PlayerGradeDetails.GetNameFromLeague(League));
                return null;
            }
            return await _queue.EnqueueLowAsync(() => OverflowSocketGuild.CreateCoopThreadHeaderAsync(gradeRole, ultraRoles, contractEmbed, category.DiscordCategory, League, GuildContract.Contract, _logger, channelIndex));
        }


        private async Task<List<OverflowServer>> GetOverflowGuildsCounts(SocketGuild guild, Guild dbguild) {
            return [
                new(){ Guild = guild },
                .. dbguild.OverflowServers.Select(os => new OverflowServer(ServerFunction.Overflow){ Guild = _client.Guilds.First(g => g.Id == os)})
            ];
        }

        public enum ServerFunction { Primary = 0, Overflow = 1 };
        public class OverflowServer(ServerFunction serverFunction = ServerFunction.Primary) {
            public SocketGuild Guild { get; set; }
            private readonly ServerFunction ServerFunction = serverFunction;
            public int ChannelsLeft {
                get {
                    if(Guild == null) return 0;
                    return (ServerFunction == ServerFunction.Primary ? PrimaryMaxChannels : OverflowMaxChannels) - Guild.GetInUseChannelCount();
                }
            }
            public int ThreadsLeft {
                get {
                    if(Guild == null) return 0;
                    return (ServerFunction == ServerFunction.Primary ? PrimaryMaxThreads : OverflowMaxThreads) - Guild.GetInUseThreadCount();
                }
            }

            private List<CoopCategories> CoopCategories { get; set; }
            public async Task<List<CoopCategories>> GetCoopCategories(DiscordHostedService discord) {
                if(Guild == null) return null;
                CoopCategories ??= [.. (await discord.GetAllCoopCategories(Guild)).Select(x => new CoopCategories(Guild, x))];
                return CoopCategories;
            }
        }

        public class CoopCategories(SocketGuild guild, SocketGuildChannel discordCategory) {
            public SocketGuild Guild { get; set; } = guild;
            public SocketGuildChannel DiscordCategory { get; set; } = discordCategory;
            public int CurrentCount {
                get {
                    if(Guild is null) return int.MaxValue;
                    return Guild.GetInUseChannelCount(DiscordCategory);
                }
            }
        }
    }
}
