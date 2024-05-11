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

namespace EGG9000.Bot.Automated.Coops {
    public class CreateCoopThreads : _UpdaterBase<CreateCoopThreads> {
        private ThreadsCoopStatusUpdater _threadsCoopStatusUpdater;
        public CreateCoopThreads(IServiceProvider provider, ThreadsCoopStatusUpdater threadsCoopStatusUpdater) : base(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(0), provider) {
            _threadsCoopStatusUpdater = threadsCoopStatusUpdater;
        }


        private const double THREAD_CREATION_DELAY = 4.5;

        public async override Task Run(object state, CancellationToken cancellationToken) {
            ulong.TryParse(_configuration.GetConnectionString("CPGuildId"), out var _CPGuildId);

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();


            List<Coop> allCoops;
            while(
                (allCoops = await _db.Coops.Include(c => c.Contract).AsQueryable().Where(x => x.ThreadID == 0 && x.DiscordChannelId == 0 && !x.ThreadArchived && !x.DeletedChannel).ToListAsync(CancellationToken.None))
                .Count > 0) {
                if(cancellationToken.IsCancellationRequested) return;

                var guildIDs = allCoops.Select(x => x.GuildId).Distinct().ToArray();


                var coops = new List<Coop>();

                while(allCoops.Count > 0 && coops.Count < 20) {
                    foreach(var guildID in guildIDs) {
                        var coop = allCoops.FirstOrDefault(x => x.GuildId == guildID);
                        if(coop != null) {
                            coops.Add(coop);
                            allCoops.Remove(coop);
                        }
                    }
                }



                var contractIDs = coops.Select(x => x.ContractID).Distinct().ToArray();
                var guildContracts = await _db.GuildContracts.Include(gc => gc.Contract).Where(gc => guildIDs.Contains(gc.GuildID) && contractIDs.Contains(gc.ContractID)).ToListAsync(cancellationToken);
                var dbguilds = await _db.Guilds.Where(x => guildIDs.Contains(x.Id)).ToListAsync(cancellationToken);

                var guildsWithOverflow = new List<(SocketGuild Guild, List<OverflowServer> Servers, DateTimeOffset LastAccessed)>();

                foreach(var guild in _client.Guilds.Where(x => coops.Any(y => y.GuildId == x.Id)).OrderBy(x => x.Id == _CPGuildId)) {
                    guildsWithOverflow.Add((guild, await GetOverflowGuildsCounts(guild, _db), DateTimeOffset.MinValue));
                }


                foreach(var coop in coops) {
                    if(cancellationToken.IsCancellationRequested) return;
                    var guildWithOverflow = guildsWithOverflow.First(x => x.Guild.Id == coop.GuildId);

                    if(guildWithOverflow.LastAccessed.AddSeconds(THREAD_CREATION_DELAY) > DateTimeOffset.Now) {
                        var timeToDelay = guildWithOverflow.LastAccessed.AddSeconds(THREAD_CREATION_DELAY) - DateTimeOffset.Now;
                        _logger.LogInformation("Delaying for {delay} on {guild}", timeToDelay.Humanize(precision: 2).ShortenTime(), guildWithOverflow.Guild.Name);
                        await Task.Delay(timeToDelay);
                    }


                    try {
                        var guildContract = guildContracts.First(gc => gc.GuildID == guildWithOverflow.Guild.Id && string.Equals(gc.ContractID, coop.ContractID, StringComparison.CurrentCultureIgnoreCase));
                        var (thread, parent) = await TryCreateCoopThread(guildContract, guildWithOverflow.Guild, coop, guildWithOverflow.Servers);
                        if(thread != null) {
                            coop.ThreadID = thread.Id;
                            coop.ThreadParentChannel = parent.Id;
                            coop.OverflowGuildId = parent.Guild.Id;
                            _logger.LogInformation("Thread created for {coopName} in {guild}", coop.Name, guildWithOverflow.Guild.Name);
                            await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);
                            guildWithOverflow.LastAccessed = DateTimeOffset.Now;

                            var slashCommands = (await guildWithOverflow.Guild.GetApplicationCommandsAsync()).ToList().Where(c => c.Type == ApplicationCommandType.Slash).ToList();
                            var users = (await _db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == coop.Id)).ToListAsync()).SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList();
                            var dbguild = dbguilds.FirstOrDefault(x => x.Id == guildWithOverflow.Guild.Id);

                            var overflowGuild = coop.OverflowGuildId > 0 ? _client.GetGuild(coop.OverflowGuildId) : guildWithOverflow.Guild;

                            await _threadsCoopStatusUpdater.ProcessCoop(coop.Id, overflowGuild, users, dbguild, slashCommands, cancellationToken); 

                        } else {
                            _logger.LogWarning("Thread NOT created for {coopName} in {guild}", coop.Name, guildWithOverflow.Guild.Name);
                        }
                    } catch(Exception ex) {
                        _logger.LogError(ex, "Error Creating Co-op Thread {coop} in {guild}", coop.Name, guildWithOverflow.Guild.Name);
                        guildWithOverflow.LastAccessed = DateTimeOffset.Now;

                    }
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

        private async Task<(IThreadChannel thread, SocketGuildChannel parentChannel)> TryCreateCoopThread(GuildContract guildContract, SocketGuild guild, Coop coop, List<OverflowServer> servers) {
            var contractEmbed = ContractUpdater.GetContractEmbed(guildContract, guild, (Ei.Contract.Types.PlayerGrade)coop.League);
            SocketGuildChannel headerChannel = null;

            //Check channels that already have an existing header for the contract
            foreach(var server in servers.Where(x => x.ThreadsLeft > 0 && x.Guild.Channels.Any(c => c.Name == $"{coop.Contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(coop.League).ToLower()}"))) {
                headerChannel = server.Guild.Channels.FirstOrDefault(c => c.Name == $"{coop.Contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(coop.League).ToLower()}");
                try {
                    return (await CreateThreadChannelAsync(coop.Name, headerChannel), headerChannel);
                } catch(TaskCanceledException) {
                    _logger.LogWarning("Canceled create thread call due to timeout on {coopname}", coop.Name);
                    continue;
                } catch(Exception) {
                    continue;
                }
            }

            //Fall back to creating a new header channel in a server
            foreach(var server in servers.Where(x => x.ThreadsLeft > 0)) {
                var categories = await server.GetCoopCategories(_client);
                foreach(var category in categories) {
                    if(headerChannel != null || category.CurrentCount >= 50) continue;

                    var gradeRoleEnum = coop.League switch {
                        5 => GuildChannelType.GradeAAA,
                        4 => GuildChannelType.GradeAA,
                        3 => GuildChannelType.GradeA,
                        2 => GuildChannelType.GradeB,
                        1 => GuildChannelType.GradeC,
                        _ => GuildChannelType.General,
                    };
                    SocketRole gradeRole = null;
                    if(gradeRoleEnum != GuildChannelType.General) {
                        gradeRole = await _client.GetRoleAsync(gradeRoleEnum, guild);
                        if(gradeRole != null && guild.Id != server.Guild.Id) {
                            gradeRole = server.Guild.Roles.FirstOrDefault(r => r.Name == gradeRole.Name);
                        }
                    }

                    List<SocketRole> ultraRoles = [];
                    var ultraStandardRole = await _client.GetRoleAsync(GuildChannelType.StandardSubscription, guild);
                    if(ultraStandardRole != null && guild.Id != server.Guild.Id) {
                        ultraStandardRole = server.Guild.Roles.FirstOrDefault(r => r.Name == ultraStandardRole.Name);
                    }
                    if(ultraStandardRole != null) ultraRoles.Add(ultraStandardRole);

                    var ultraProRole = await _client.GetRoleAsync(GuildChannelType.ProSubscription, guild);
                    if(ultraProRole != null && guild.Id != server.Guild.Id) {
                        ultraProRole = server.Guild.Roles.FirstOrDefault(r => r.Name == ultraProRole.Name);
                    }
                    if(ultraProRole != null) ultraRoles.Add(ultraProRole);

                    headerChannel = await server.Guild.CreateCoopThreadHeaderAsync(gradeRole, ultraRoles, contractEmbed, category.DiscordCategory, coop, _logger);
                }
                if(headerChannel == null) continue;
                try {
                    return (await CreateThreadChannelAsync(coop.Name, headerChannel), headerChannel);
                } catch(TaskCanceledException) {
                    _logger.LogWarning("Canceled create thread call due to timeout on {coopname}", coop.Name);
                } catch(Exception) { }
            }

            return (null, null);
        }


        private async Task<List<OverflowServer>> GetOverflowGuildsCounts(SocketGuild guild, ApplicationDbContext db) {
            var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
            if(dbguild == null) { return null; }
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
