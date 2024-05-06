using Discord;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

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

namespace EGG9000.Bot.Automated.Coops {
    public class CreateCoopThreads(IServiceProvider provider) : _UpdaterBase<CreateCoopThreads>(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coops = await _db.Coops.Include(c => c.Contract).AsQueryable().Where(x => x.ThreadID == 0 && x.DiscordChannelId == 0 && !x.ThreadArchived && !x.DeletedChannel).ToListAsync(CancellationToken.None);

            if(coops is null || coops.Count == 0) {
                return;
            }

            var guildIDs = coops.Select(x => x.GuildId).Distinct().ToArray();
            var contractIDs = coops.Select(x => x.ContractID).Distinct().ToArray();
            var guildContracts = await _db.GuildContracts.Include(gc => gc.Contract).Where(gc => guildIDs.Contains(gc.GuildID) && contractIDs.Contains(gc.ContractID)).ToListAsync(cancellationToken);


            foreach(var coopGroup in coops.GroupBy(x => x.GuildId)) {
                if(cancellationToken.IsCancellationRequested)
                    continue;

                var guild = _client.Guilds.FirstOrDefault(x => x.Id == coopGroup.Key);
                if(guild is null) {
                    _logger.LogError("Unable to load guild with the id {guildid}", coopGroup.Key);
                    continue;
                }

                var servers = await GetOverflowGuildsCounts(guild, _db);
                var dbguild = await _db.Guilds.Where(x => x.OverflowServersJson.Contains(coopGroup.Key.ToString())).FirstOrDefaultAsync(CancellationToken.None);
                if(servers == null) {
                    if(dbguild == null) {
                        _logger.LogWarning("Co-op is trying to be made for guild that is not registered, {guildname} {guildid}, Co-op Name {coop}, Users {user}",
                            guild.Name, guild.Id, coopGroup.First().Name,
                            string.Join(", ", await _db.UserCoopXrefs.Where(x => x.CoopId == coopGroup.First().Id).Select(x => x.User.DiscordUsername).ToListAsync(CancellationToken.None))
                        );
                        continue;
                    }
                    guild = _client.Guilds.First(X => X.Id == dbguild.Id);
                    servers = await GetOverflowGuildsCounts(guild, _db);
                    foreach(var coop in coopGroup) {
                        coop.GuildId = dbguild.Id;
                    }
                }
                _logger.LogInformation("Coop Counts {count} {guild}", coopGroup.Count(), guild.Name);

                foreach(var coop in coopGroup) {
                    if(cancellationToken.IsCancellationRequested)
                        continue;

                    try {
                        var guildContract = guildContracts.First(gc => gc.GuildID == guild.Id && string.Equals(gc.ContractID, coop.ContractID, StringComparison.CurrentCultureIgnoreCase));
                        var (thread, parent) = await TryCreateCoopThread(guildContract, guild, coop, servers);
                        if(thread != null) {
                            coop.ThreadID = thread.Id;
                            coop.ThreadParentChannel = parent.Id;
                            coop.OverflowGuildId = parent.Guild.Id;
                            _logger.LogInformation("Thread created for {coopName}", coop.Name);
                            await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);
                        } else {
                            _logger.LogWarning("Thread NOT created for {coopName}", coop.Name);
                        }
                    } catch(Exception ex) {
                        _logger.LogError(ex, "Error Creating Co-op Thread {coop} in {guild}", coop.Name, guild.Name);
                    }
                }
            }
        }

        private async Task<IThreadChannel> CreateThreadChannelAsync(string threadName, SocketGuildChannel parentChannel) {
            try {
                var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                var thread = await (parentChannel as SocketTextChannel).CreateThreadAsync(
                    name: threadName,
                    type: ThreadType.PrivateThread,
                    autoArchiveDuration: ThreadArchiveDuration.OneWeek, //Initially one week (don't archive)
                    invitable: false,
                    options: new RequestOptions {
                        RatelimitCallback = RateLimit, CancelToken = cts.Token
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

        private Task RateLimit(IRateLimitInfo info) {
            _logger.LogWarning("Rate Limit - Limit:{Limit} Remaining:{Remaining} RetryAfter:{RetryAfter} Reset:{Reset} ResetAfter:{After}",
                               info.Limit,
                               info.Remaining,
                               TimeSpan.FromSeconds(info.RetryAfter ?? 0).Humanize(precision: 2).ShortenTime(),
                               info.Reset?.Humanize().ShortenTime(),
                               info.ResetAfter?.Humanize(precision: 2).ShortenTime());
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
                    return (ServerFunction == ServerFunction.Primary ? 450 : 495) - Guild.GetInUseChannelCount();
                }
            }
            public int ThreadsLeft {
                get {
                    if(Guild == null) return 0;
                    return (ServerFunction == ServerFunction.Primary ? 975 : 995) - Guild.GetInUseThreadCount();
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
