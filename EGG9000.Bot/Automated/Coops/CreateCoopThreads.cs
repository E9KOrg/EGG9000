using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;


using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Common.Services;
using EGG9000.Common.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AutoMapper.Internal;
using MassTransit.Initializers;
using Discord.Net;
using MassTransit.Util;
using Humanizer;

namespace EGG9000.Bot.Automated.Coops {
    public class CreateCoopThreads(IServiceProvider provider) 
            : _UpdaterBase<CreateCoopThreads>(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(0), provider) {

        private static readonly int _maxDegreeOfParallelism = 2;
        private static readonly SemaphoreSlim _semaphore = new(_maxDegreeOfParallelism);

        public async override Task Run(object state, CancellationToken cancellationToken) {
            
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coops = await _db.Coops.AsQueryable().Where(x => x.ThreadID == 0 && !x.DeletedChannel).ToListAsync(cancellationToken);

            if(coops is null || coops.Count == 0) {
                return;
            }

            foreach(var coopGroups in coops.GroupBy(x => x.GuildId)) {
                if (cancellationToken.IsCancellationRequested)
                    continue;

				var guild = _client.Guilds.First(x => x.Id == coopGroups.Key);
				var servers = await GetOverflowGuildsCounts(guild, _db);
				var dbguild = await _db.Guilds.Where(x => x.OverflowServersJson.Contains(coopGroups.Key.ToString())).FirstOrDefaultAsync(cancellationToken);
				if (servers == null) {
                    if(dbguild == null) {
                        _logger.LogWarning("Co-op is trying to be made for guild that is not registered, {guildname} {guildid}, Co-op Name {coop}, Users {user}",
                            guild.Name, guild.Id, coopGroups.First().Name,
                            string.Join(", ", await _db.UserCoopXrefs.Where(x => x.CoopId == coopGroups.First().Id).Select(x => x.User.DiscordUsername).ToListAsync(cancellationToken))
                        );
                        continue;
                    }
                    guild = _client.Guilds.First(X => X.Id == dbguild.Id);
                    servers = await GetOverflowGuildsCounts(guild, _db);
                    foreach(var coopGroup in coopGroups) {
                        coopGroup.GuildId = dbguild.Id;
                    }
                }

				var completedCoops = await _db.Coops.AsQueryable().Where(x => !x.DeletedChannel && (x.Status == CoopStatusEnum.Completed || x.Status == CoopStatusEnum.Failed)).OrderBy(x => x.CoopCompleted).ToListAsync();
				_logger.LogInformation("Coop Counts {count} {guild}", coopGroups.Count(), guild.Name);

                foreach(var coop in coopGroups) {
                    if(cancellationToken.IsCancellationRequested)
                        continue;

                    try {
                        var headerChannels = dbguild.CoopThreadHeaders[coop.Contract][coop.League];
						var (thread, parent) = await TryCreateCoopThread(guild, headerChannels, coop, servers, completedCoops, cancellationToken);
                        if(thread != null) {
                            coop.ThreadID = thread.Id;
                            coop.ThreadParentChannel = parent.Id;
                            coop.OverflowGuildId = parent.Guild.Id;
                            _logger.LogInformation("Thread created for {coopName}", coop.Name);
                            try {
                                await _db.SaveChangesAsync(cancellationToken);
                            } catch(Exception) {
                                await _db.SaveChangesAsync(cancellationToken);
                            }
                        } else {
                            _logger.LogWarning("Thread NOT created for {coopName}", coop.Name);
                        }
                    } catch(Exception ex) {
                        _logger.LogError(ex, "Error Creating Co-op Thread {coop} in {guild}", coop.Name, guild.Name);
                    }
                }
			}
        }

		private static async Task<IThreadChannel> CreateThreadChannelAsync(SocketGuild guild, string threadName, SocketGuildChannel parentChannel, CancellationToken cancellationToken) {
            try {
                return await (parentChannel as SocketTextChannel).CreateThreadAsync(
                    threadName,
                    ThreadType.PrivateThread,
                    ThreadArchiveDuration.ThreeDays,
                    invitable: false
                );
            } catch(HttpException dException) {
                if(dException.DiscordCode == DiscordErrorCode.MaximumActiveThreadsReached) {
                    //Expected?
                }
            } catch(Exception) { }
            return null;
        }

        private async Task<(IThreadChannel thread, SocketGuildChannel parentChannel)> TryCreateCoopThread(SocketGuild guild, Dictionary<ulong, ulong> headerChannels, Coop coop, List<OverflowServer> servers, List<Coop> completedCoops, CancellationToken cancellationToken) {
            SocketGuildChannel headerChannel = null;
            foreach(var server in servers.Where(x => x.ThreadsLeft > 0)) {
                if (!headerChannels.TryGetValue(server.Guild.Id, out var headerChannelId)) continue;
                headerChannel = guild.GetChannel(headerChannelId);
                if (headerChannel == null) continue;
                try {
                    return (await CreateThreadChannelAsync(server.Guild, coop.Name, headerChannel, cancellationToken), headerChannel);
                }
                catch (Exception) { }
			}

            if(completedCoops is null || completedCoops.Count == 0) {
                return (null, null);
            }

            var completedCoop = completedCoops.First();
            completedCoops.Remove(completedCoop);
            var completedCoopParentChannel = (_client.GetChannel(completedCoop.ThreadParentChannel) as SocketTextChannel);
			var coopThread = completedCoopParentChannel.Threads.FirstOrDefault(t => !t.IsArchived && t.Id == completedCoop.ThreadID);
            if(coopThread != null) {
                try {
                    await coopThread.ModifyAsync(a => a.Archived = true);
				} catch (Exception) {
					await coopThread.ModifyAsync(a => a.Archived = true);
				}
				coopThread = completedCoopParentChannel.Threads.FirstOrDefault(t => !t.IsArchived && t.Id == completedCoop.ThreadID);
                if(coopThread == null){
					var server = servers.Where(x => x.Guild.Id == completedCoop.GuildId).FirstOrDefault();
					return (await CreateThreadChannelAsync(server.Guild, coop.Name, headerChannel, cancellationToken), headerChannel);
                } else {
                    _logger.LogInformation("Unable to archive co-op thread for {coop} - was not able to free up space, re-iterating", coop.Name);
                }
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

        public enum ServerFunction { Primary = 0, Overflow = 1};
        public class OverflowServer(ServerFunction serverFunction = ServerFunction.Primary) {
            public SocketGuild Guild { get; set; }
            private readonly ServerFunction ServerFunction = serverFunction;
            public int ChannelsLeft { 
                get {
                    if(Guild == null) return 0;
                    return (ServerFunction == ServerFunction.Primary ? 450 : 500) - Guild.GetInUseChannelCount();
                }
            }
            public int ThreadsLeft {
                get {
                    if (Guild == null) return 0;
                    return (ServerFunction == ServerFunction.Primary ? 900 : 1000) - Guild.GetInUseThreadCount();
                }
            }

			private List<CoopCategories> CoopCategories { get; set; }
            public async Task<List<CoopCategories>> GetCoopCategories(DiscordHostedService discord) {
                if(Guild == null) return null;
                if(CoopCategories == null)
                    CoopCategories = (await discord.GetAllCoopCategories(Guild)).Select(x => new CoopCategories(Guild, x)).ToList();
                return CoopCategories;
            }
        }

        public class CoopCategories(SocketGuild guild, SocketGuildChannel discordCategory) {
            public SocketGuild Guild { get; set; } = guild;
            public SocketGuildChannel DiscordCategory { get; set; } = discordCategory;
            public int CurrentCount { 
                get {
                    if(Guild is null) return 0;
                    return Guild.GetInUseChannelCount(DiscordCategory);
                } 
            }
        }
    }
}
