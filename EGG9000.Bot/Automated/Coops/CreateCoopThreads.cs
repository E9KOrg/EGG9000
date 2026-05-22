using Discord;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using static EGG9000.Common.Helpers.CreateCoopsV2;

using Humanizer;

using MassTransit.Initializers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using static EGG9000.Common.Helpers.Prefarm;
using System.Collections.Concurrent;
using MassTransit.Internals;
using Microsoft.Extensions.Caching.Memory;
using static Ei.Contract.Types;
using EGG9000.Bot.Services;
using EGG9000.Bot.EggIncAPI;
using MassTransit;

namespace EGG9000.Bot.Automated.Coops {
    public class CreateCoopThreads(IServiceProvider provider, ThreadsCoopStatusUpdater threadsCoopStatusUpdater) : _UpdaterBase<CreateCoopThreads>(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(0), provider) {
        private ThreadsCoopStatusUpdater _threadsCoopStatusUpdater = threadsCoopStatusUpdater;

        private readonly Dictionary<string, int> CoopsTimeoutCounter = new();
        private readonly Dictionary<CreatorInfo, DateTimeOffset> CreatorLastUsed = new();
        public class CreatorInfo {
            public string EggIncId { get; set; }
            public string ContractId { get; set; }
            public PlayerGrade Grade { get; set; }

            public override bool Equals(object obj) {
                if(obj is CreatorInfo other) {
                    return EggIncId == other.EggIncId && ContractId == other.ContractId && Grade == other.Grade;
                }
                return false;
            }

            public override int GetHashCode() {
                return HashCode.Combine(EggIncId, ContractId, Grade);
            }
        }



        public async override Task Run(object state, CancellationToken cancellationToken) {
            //return;
            ulong.TryParse(_configuration.GetConnectionString("CPGuildId"), out var _CPGuildId);

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();


            List<Coop> allCoops;
            var throttler = new SemaphoreSlim(5);
            var tasks = new List<Task>();

            var dbguilds = _db.CachedGuilds.ToList();

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
                    _coopsBeingCreatedService.SetCoopsAreBeingCreated(true);
                }
                foreach(var coop in coops) {
                    StillAlive();
                    if(coop.ContractID is null) {
                        if(CoopsTimeoutCounter.ContainsKey(coop.Name)) {
                            if(CoopsTimeoutCounter[coop.Name] > 60) {
                                _logger.LogWarning("Unable to create channel for coop {coop} because the contract is null", coop.Name);
                                CoopsTimeoutCounter[coop.Name] = 0;
                            } else {
                                CoopsTimeoutCounter[coop.Name]++;
                                if(allCoops.All(x => CoopsTimeoutCounter.ContainsKey(x.Name))) {
                                    goto ExitWhile;
                                }
                            }
                        } else {
                            _logger.LogWarning("Unable to create channel for coop {coop} because the contract is null", coop.Name);
                            CoopsTimeoutCounter.Add(coop.Name, 1);
                        }
                        continue;
                    }
                    if(cancellationToken.IsCancellationRequested) return;
                    var guildWithOverflow = guildsWithOverflow.First(x => x.Guild.Id == coop.GuildId);

                    //if(guildWithOverflow.LastAccessed.AddSeconds(THREAD_CREATION_DELAY) > DateTimeOffset.Now) {
                    //    var timeToDelay = guildWithOverflow.LastAccessed.AddSeconds(THREAD_CREATION_DELAY) - DateTimeOffset.Now;
                    //    _logger.LogInformation("Delaying for {delay} on {guild}", timeToDelay.Humanize(precision: 2).ShortenTime(), guildWithOverflow.Guild.Name);
                    //    await Task.Delay(timeToDelay);
                    //}


                    try {
                        var guildContract = guildContracts.First(gc => gc.GuildID == guildWithOverflow.Guild.Id && string.Equals(gc.ContractID, coop.ContractID, StringComparison.CurrentCultureIgnoreCase));
                        var secondsRemaining = Math.Max(guildContract.Contract.Details.LengthSeconds, TimeSpan.FromDays(1.6).TotalSeconds);

                        if(!coop.AddedFromBackup) {
                            var creator = ContractsAPI.CoopCreatorIds.FirstOrDefault(x => x.EggIncId == coop.CreatorID);
                            await CreateCoopViaApi(coop.ContractID, (PlayerGrade)coop.League, coop.Name, secondsRemaining, coop.CreatorID, coop.AnyLeague, kickCreator: creator == default);

                            if(creator != default) {
                                CreatorLastUsed[new CreatorInfo() { EggIncId = creator.EggIncId, ContractId = coop.ContractID, Grade = (PlayerGrade)coop.League }] = DateTimeOffset.Now;
                            }
                        }

                        var headerChannel = await GetHeaderChannelAndWait(headerChannels, coop);
                        if(headerChannel == null) {
                            _logger.LogError("Unable to get header channel for {coop} in contract {contract}", coop.Name, guildContract.ContractID);
                        } else {
                            if(coop.ThreadID > 0) {
                                var existingThread = (SocketThreadChannel)await _client.GetChannelAsync(coop.ThreadID);
                                _logger.LogWarning("Trying to create a new thread for {coop} already has a thread at {thread}", coop.Name, existingThread?.Name ?? "null");
                                continue;
                            }
                            var coopThread = await _queue.EnqueueLowAsync<IThreadChannel>(() => CreateThreadChannelAsync(coop.Name, headerChannel));

                            if(coopThread != null) {
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
                                guildWithOverflow.LastAccessed = DateTimeOffset.Now;
                                var dbguild = dbguilds.FirstOrDefault(x => x.Id == guildWithOverflow.Guild.Id);
                                var overflowGuild = coop.OverflowGuildId > 0 ? _client.GetGuild(coop.OverflowGuildId) : guildWithOverflow.Guild;


                                while(!await throttler.WaitAsync(20000, cancellationToken)) {
                                    _logger.LogInformation("Waiting on throttle");
                                }

                                tasks.Add(Task.Run(async () => {
                                    try {
                                        var db2 = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                        //var users = (await db2.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == coop.Id)).ToListAsync()).SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList();
                                        var xrefs = await db2.UserCoopXrefs.Include(x => x.User).AsQueryable().Where(x => x.CoopId == coop.Id).ToListAsync();
                                        var coopToUpdate = await db2.Coops.FirstAsync(c => c.Id == coop.Id);

                                        var capturedThread = coopThread;
                                        var introText = $"Coop **{coop.Name}** for the contract **{guildContract.Contract.Name}** is ready for the following to join: {string.Join(", ", xrefs.Select(x => $"<@{x.User.DiscordId}>" + (x.User.EggIncAccounts.Count > 1 ? $"({x.User.EggIncAccounts.FirstOrDefault(e => e.Id == x.EggIncId)?.Backup.UserName ?? "Check website"})" : "")))}\n";
                                        var msgIds = await _queue.EnqueueLowAsync<List<ulong>>(async () => {
                                            var m1 = await capturedThread.SendMessageAsync(introText);
                                            var m2 = await capturedThread.SendMessageAsync("\u17B5");
                                            var m3 = await capturedThread.SendMessageAsync("\u17B5");
                                            var m4 = await capturedThread.SendMessageAsync("\u17B5");
                                            var m5 = await capturedThread.SendMessageAsync("\u17B5");
                                            return new List<ulong> { m1.Id, m2.Id, m3.Id, m4.Id, m5.Id };
                                        });
                                        coopToUpdate.UpdateMessagesId = JsonConvert.SerializeObject(msgIds);
                                        await db2.SaveChangesAsyncRetry(cancellationToken: cancellationToken);
                                    } finally {
                                        throttler.Release();
                                    }
                                }, cancellationToken));
                            } else {
                                _logger.LogWarning("Thread NOT created for {coopName} in {guild}", coop.Name, guildWithOverflow.Guild.Name);
                            }
                        }
                    } catch(Exception ex) {
                        _logger.LogError(ex, "Error Creating Co-op Thread {coop} in {guild}", coop.Name, guildWithOverflow.Guild.Name);
                        //guildWithOverflow.LastAccessed = DateTimeOffset.Now;

                    }
                }
            }
            _coopsBeingCreatedService.SetCoopsAreBeingCreated(false);
        ExitWhile:

            if(tasks.Count > 0) {
                var watchdogCancellationSource = new CancellationTokenSource();
                var watchdogCancelToken = watchdogCancellationSource.Token;
                var watchdogTask = Task.Delay(TimeSpan.FromMinutes(10), watchdogCancelToken);
                var allTasks = Task.WhenAll(tasks);
                var completedTask = await Task.WhenAny(allTasks, watchdogTask);

                if(completedTask == watchdogTask) { // Timeout occurred
                    watchdogCancellationSource.Cancel();
                    _logger.LogWarning("Watchdog Task Called, not all tasks finished. (Completed {count} of {total})", tasks.Count(x => x.IsCompleted), tasks.Count);
                }
                _logger.LogInformation("Finished created {count} co-ops", tasks.Count);
            }

            await MoveCreatorsToBlankCoop();
        }

        private async Task MoveCreatorsToBlankCoop() {
            // Collect keys to remove first to avoid modifying the dictionary while iterating
            var now = DateTimeOffset.Now;
            var expiredCreators = CreatorLastUsed
                .Where(kv => kv.Value.AddMinutes(2) < now)
                .Select(kv => kv.Key)
                .ToList();

            foreach(var creator in expiredCreators) {
                try {
                    var blankCoopName = $"bc{DateTimeOffset.Now.ToUnixTimeSeconds()}";
                    _logger.LogInformation("Moving creator {creator} {grade} to blank coop {coop}", creator.EggIncId, creator.Grade, blankCoopName);
                    await CreateCoopViaApi(creator.ContractId, creator.Grade, blankCoopName, 3600, creator.EggIncId, false, kickCreator: false);
                } catch(Exception ex) {
                    _logger.LogError(ex, "Failed to move creator {creator} to blank coop", creator.EggIncId);
                    continue;
                } finally {
                    // Remove after attempt to ensure the entry is cleaned up regardless of success/failure
                    CreatorLastUsed.Remove(creator);
                }
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
                        RatelimitCallback = (IRateLimitInfo info) => RateLimit(info, threadName), CancelToken = cts.Token
                    }
                );
                cts.Dispose();
                return thread;
            } catch(HttpException dException) {
                if(dException.DiscordCode == DiscordErrorCode.MaximumActiveThreadsReached) {
                    //Expected?
                }
            } catch(Exception) { }
            return null;
        }

        private Task RateLimit(IRateLimitInfo info, string threadName) {
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
            public List<ServerHeaderChannel> HeaderChannels = new();
            public List<LastAccessedByServer> LastAccessed = new();
        }

        public class ServerHeaderChannel {
            public uint League { get; set; }
            public string ContractId { get; set; }
            public ulong ServerId { get; set; }
            public SocketGuildChannel HeaderChannel { get; set; }
        }

        public class LastAccessedByServer {
            public ulong ServerId { get; set; }
            public DateTimeOffset LastAccessed { get; set; }
        }

        object __headerChannelLock = new object();
        private Task<SocketGuildChannel> GetHeaderChannelAndWait(List<HeaderChannelsForGuild> headerChannels, Coop coop) {
            SocketGuildChannel headerChannel;
            lock(__headerChannelLock) {
                var headerChannelsForGuild = headerChannels.First(x => x.GuildId == coop.GuildId);
                var currentChannels = headerChannelsForGuild.HeaderChannels.Where(x => x.League == coop.League && x.ContractId == coop.ContractID).ToList();
                var lastAccessedObject = headerChannelsForGuild.LastAccessed.Where(la => currentChannels.Any(x => x.ServerId == la.ServerId)).OrderBy(la => la.LastAccessed).First();
                var serverHeaderChannel = currentChannels.First(x => x.ServerId == lastAccessedObject.ServerId);
                headerChannel = serverHeaderChannel.HeaderChannel;
                lastAccessedObject.LastAccessed = DateTimeOffset.Now;
            }

            return Task.FromResult(headerChannel);
        }

        private async Task<List<HeaderChannelsForGuild>> GetOrCreateHeaderChannelsForCoops(ApplicationDbContext db, List<Coop> coops, List<Guild> guilds, List<GuildContract> guildContracts) {
            List<HeaderChannelsForGuild> headerChannelsForGuilds = new();
            foreach(var guild in guilds) {
                HeaderChannelsForGuild headerChannelsForGuild = new HeaderChannelsForGuild { GuildId = guild.Id };
                headerChannelsForGuilds.Add(headerChannelsForGuild);
                headerChannelsForGuild.LastAccessed.Add(new LastAccessedByServer { ServerId = guild.Id, LastAccessed = DateTimeOffset.MinValue });
                headerChannelsForGuild.LastAccessed.AddRange(guild.OverflowServers.Select(x => new LastAccessedByServer { ServerId = x, LastAccessed = DateTimeOffset.MinValue }));

                var contractGroups = coops.Where(x => x.GuildId == guild.Id).GroupBy(x => new { x.ContractID, x.GuildId, x.League });
                foreach(var contractGroup in contractGroups) {
                    var mainServer = _client.Guilds.First(x => x.Id == guild.Id);
                    var guildContract = guildContracts.First(x => x.GuildID == guild.Id && x.ContractID == contractGroup.Key.ContractID);
                    if(guild.OverflowServers.Any() && PlayerGradeDetails.GetGradeFromLeague(contractGroup.Key.League) == Ei.Contract.Types.PlayerGrade.GradeAaa) {
                        if(contractGroup.Count() > 2) {
                            foreach(var overflow in guild.OverflowServers) {
                                var overflowServer = _client.Guilds.First(x => x.Id == overflow);

                                var headerChannel = await GetOrCreateHeaderChannel(db, contractGroup.Key.League, overflowServer, mainServer, guildContract);
                                headerChannelsForGuild.HeaderChannels.Add(new ServerHeaderChannel { ContractId = contractGroup.Key.ContractID, HeaderChannel = headerChannel, ServerId = overflowServer.Id, League = contractGroup.Key.League });

                            }
                        } else {
                            var overflowServer = _client.Guilds.First(x => x.Id == guild.OverflowServers.First());
                            var headerChannel = await GetOrCreateHeaderChannel(db, contractGroup.Key.League, overflowServer, mainServer, guildContract);
                            headerChannelsForGuild.HeaderChannels.Add(new ServerHeaderChannel { ContractId = contractGroup.Key.ContractID, HeaderChannel = headerChannel, ServerId = mainServer.Id, League = contractGroup.Key.League });

                        }
                    } else {
                        var headerChannel = await GetOrCreateHeaderChannel(db, contractGroup.Key.League, mainServer, mainServer, guildContract);
                        headerChannelsForGuild.HeaderChannels.Add(new ServerHeaderChannel { ContractId = contractGroup.Key.ContractID, HeaderChannel = headerChannel, ServerId = mainServer.Id, League = contractGroup.Key.League});
                    }
                }
            }
            return headerChannelsForGuilds;
        }


        private async Task<SocketGuildChannel> GetOrCreateHeaderChannel(ApplicationDbContext db, uint League, SocketGuild OverflowSocketGuild, SocketGuild MainSocketGuild, GuildContract GuildContract) {
            var headerChannel = OverflowSocketGuild.Channels.FirstOrDefault(c => c.Name == $"{GuildContract.Contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(League).ToLower()}");
            if(headerChannel != null) {
                return headerChannel;
            }

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

            _logger.LogInformation("Creating header channel for {contract} {grade} in {server}", GuildContract.Contract.GetE9KName(), PlayerGradeDetails.GetNameFromLeague(League), OverflowSocketGuild.Name);

            var contractEmbed = await ContractUpdater.GetContractEmbed(GuildContract, db, MainSocketGuild, (Ei.Contract.Types.PlayerGrade)League);
            var categories = (await _client.GetAllCoopCategories(OverflowSocketGuild))?.Select(x => new CoopCategories(OverflowSocketGuild, x)).ToList() ?? [];
            var category = categories?.OrderBy(x => x.DiscordCategory.Position)?.FirstOrDefault(x => x.CurrentCount < 50);

#if DEV9002
            if(category == null) {
                var newCategory = await OverflowSocketGuild.CreateCategoryChannelAsync("Coops");
                categories = (await _client.GetAllCoopCategories(OverflowSocketGuild)).Select(x => new CoopCategories(OverflowSocketGuild, x)).ToList();
                category = categories.OrderBy(x => x.DiscordCategory.Position).FirstOrDefault(x => x.CurrentCount < 50);
            }
#endif
            if(category == null) {
                _logger.LogError("No coop category with available space found in {server} for {contract} grade {grade}", OverflowSocketGuild.Name, GuildContract.Contract.GetE9KName(), PlayerGradeDetails.GetNameFromLeague(League));
                return null;
            }
            return await _queue.EnqueueLowAsync<SocketGuildChannel>(() => OverflowSocketGuild.CreateCoopThreadHeaderAsync(gradeRole, ultraRoles, contractEmbed, category.DiscordCategory, League, GuildContract.Contract, _logger));
        }

        //private async Task<(IThreadChannel thread, SocketGuildChannel parentChannel)> TryCreateCoopThread(GuildContract guildContract, SocketGuild guild, Coop coop, List<OverflowServer> servers) {
        //    var contractEmbed = ContractUpdater.GetContractEmbed(guildContract, guild, (Ei.Contract.Types.PlayerGrade)coop.League);
        //    SocketGuildChannel headerChannel = null;

        //    //Check channels that already have an existing header for the contract
        //    foreach(var server in servers.Where(x => x.ThreadsLeft > 0 && x.Guild.Channels.Any(c => c.Name == $"{coop.Contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(coop.League).ToLower()}"))) {
        //        headerChannel = server.Guild.Channels.FirstOrDefault(c => c.Name == $"{coop.Contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(coop.League).ToLower()}");
        //        if(headerChannel is not null)
        //            break;
        //    }

        //    if(headerChannel is null) {
        //        //Fall back to creating a new header channel in a server
        //        foreach(var server in servers.Where(x => x.ThreadsLeft > 0)) {
        //            var categories = await server.GetCoopCategories(_client);
        //            foreach(var category in categories.Where(x => x.CurrentCount < 50)) {
        //                var gradeRoleEnum = coop.League switch {
        //                    5 => GuildChannelType.GradeAAA,
        //                    4 => GuildChannelType.GradeAA,
        //                    3 => GuildChannelType.GradeA,
        //                    2 => GuildChannelType.GradeB,
        //                    1 => GuildChannelType.GradeC,
        //                    _ => GuildChannelType.General,
        //                };
        //                SocketRole gradeRole = null;
        //                if(gradeRoleEnum != GuildChannelType.General) {
        //                    gradeRole = await _client.GetRoleAsync(gradeRoleEnum, guild);
        //                    if(gradeRole != null && guild.Id != server.Guild.Id) {
        //                        gradeRole = server.Guild.Roles.FirstOrDefault(r => r.Name == gradeRole.Name);
        //                    }
        //                }

        //                List<SocketRole> ultraRoles = [];
        //                var ultraStandardRole = await _client.GetRoleAsync(GuildChannelType.StandardSubscription, guild);
        //                if(ultraStandardRole != null && guild.Id != server.Guild.Id) {
        //                    ultraStandardRole = server.Guild.Roles.FirstOrDefault(r => r.Name == ultraStandardRole.Name);
        //                }
        //                if(ultraStandardRole != null) ultraRoles.Add(ultraStandardRole);

        //                var ultraProRole = await _client.GetRoleAsync(GuildChannelType.ProSubscription, guild);
        //                if(ultraProRole != null && guild.Id != server.Guild.Id) {
        //                    ultraProRole = server.Guild.Roles.FirstOrDefault(r => r.Name == ultraProRole.Name);
        //                }
        //                if(ultraProRole != null) ultraRoles.Add(ultraProRole);

        //                _logger.LogInformation("Creating header channel for {contract} {grade} in {server}", coop.Contract.GetE9KName(), PlayerGradeDetails.GetNameFromLeague(coop.League), server.Guild.Name);
        //                headerChannel = await server.Guild.CreateCoopThreadHeaderAsync(gradeRole, ultraRoles, contractEmbed, category.DiscordCategory, coop.League, coop.Contract, _logger);
        //                if(headerChannel is not null) break;
        //            }
        //            if(headerChannel is not null) break;
        //        }
        //    }

        //    try {
        //        return (await CreateThreadChannelAsync(coop.Name, headerChannel), headerChannel);
        //    } catch(TaskCanceledException) {
        //        _logger.LogWarning("Canceled create thread call due to timeout on {coopname}", coop.Name);
        //    } catch(Exception e) {
        //        _logger.LogError(e, "Error creating coop thread");
        //    }

        //    return (null, null);
        //}


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
                CoopCategories ??= (await discord.GetAllCoopCategories(Guild)).Select(x => new CoopCategories(Guild, x)).ToList();
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
