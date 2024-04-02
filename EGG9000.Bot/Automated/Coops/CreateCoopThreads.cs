using Discord;
using Discord.Net;
using Discord.WebSocket;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;
using MassTransit.Initializers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated.Coops {
    public class CreateCoopThreads(IServiceProvider provider) : _UpdaterBase<CreateCoopThreads>(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coops = await _db.Coops.Include(c => c.Contract).AsQueryable().Where(x => x.ThreadID == 0 && x.DiscordChannelId == 0 && !x.ThreadArchived).ToListAsync(CancellationToken.None);

            if(coops is null || coops.Count == 0) {
                return;
            }

            foreach(var coopGroups in coops.GroupBy(x => x.GuildId)) {
                if (cancellationToken.IsCancellationRequested)
                    continue;

				var guild = _client.Guilds.FirstOrDefault(x => x.Id == coopGroups.Key);
                if(guild is null) {
                    _logger.LogError("Unable to load guild with the id {guildid}", coopGroups.Key);
                    continue;
                }

                var guildContracts = await _db.GuildContracts.Include(gc => gc.Contract).Where(gc => gc.GuildID == guild.Id).ToListAsync(cancellationToken);
                var servers = await GetOverflowGuildsCounts(guild, _db);
				var dbguild = await _db.Guilds.Where(x => x.OverflowServersJson.Contains(coopGroups.Key.ToString())).FirstOrDefaultAsync(CancellationToken.None);
				if (servers == null) {
                    if(dbguild == null) {
                        _logger.LogWarning("Co-op is trying to be made for guild that is not registered, {guildname} {guildid}, Co-op Name {coop}, Users {user}",
                            guild.Name, guild.Id, coopGroups.First().Name,
                            string.Join(", ", await _db.UserCoopXrefs.Where(x => x.CoopId == coopGroups.First().Id).Select(x => x.User.DiscordUsername).ToListAsync(CancellationToken.None))
                        );
                        continue;
                    }
                    guild = _client.Guilds.First(X => X.Id == dbguild.Id);
                    servers = await GetOverflowGuildsCounts(guild, _db);
                    foreach(var coopGroup in coopGroups) {
                        coopGroup.GuildId = dbguild.Id;
                    }
                }

                var completedCoops = await _db.Coops.AsQueryable().Where(x => x.ThreadID != 0 && (x.Status == CoopStatusEnum.Completed || x.Status == CoopStatusEnum.Failed)).OrderBy(x => x.CoopCompleted).ToListAsync(CancellationToken.None);
				_logger.LogInformation("Coop Counts {count} {guild}", coopGroups.Count(), guild.Name);

                foreach(var coop in coopGroups) {
                    if(cancellationToken.IsCancellationRequested)
                        continue;

                    try {
						var (thread, parent) = await TryCreateCoopThread(guildContracts.First(gc => string.Equals(gc.ContractID, coop.ContractID, StringComparison.CurrentCultureIgnoreCase)), guild, coop, servers, completedCoops);
                        if(thread != null) {
                            coop.ThreadID = thread.Id;
                            coop.ThreadParentChannel = parent.Id;
                            coop.OverflowGuildId = parent.Guild.Id;
                            _logger.LogInformation("Thread created for {coopName}", coop.Name);
                            try {
                                await _db.SaveChangesAsync(CancellationToken.None);
                            } catch(Exception) {
                                await _db.SaveChangesAsync(CancellationToken.None);
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

		private static async Task<IThreadChannel> CreateThreadChannelAsync(string threadName, SocketGuildChannel parentChannel) {
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

        private async Task<(IThreadChannel thread, SocketGuildChannel parentChannel)> TryCreateCoopThread(GuildContract guildContract, SocketGuild guild, Coop coop, List<OverflowServer> servers, List<Coop> completedCoops) {
            var contractEmbed = ContractUpdater.GetContractEmbed(guildContract, guild,(Ei.Contract.Types.PlayerGrade)coop.League);
            SocketGuildChannel headerChannel = null;

            //Check channels that already have an existing header for the contract
            foreach(var server in servers.Where(x => x.ThreadsLeft > 0 && x.Guild.Channels.Any(c => c.Name == $"{coop.Contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(coop.League).ToLower()}"))) {
                headerChannel = server.Guild.Channels.FirstOrDefault(c => c.Name == $"{coop.Contract.GetE9KName()}-{PlayerGradeDetails.GetNameFromLeague(coop.League).ToLower()}");
                try {
                    return (await CreateThreadChannelAsync(coop.Name, headerChannel), headerChannel);
                } catch(Exception) { continue; }
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
                    }

                    headerChannel = await guild.CreateCoopThreadHeaderAsync(gradeRole, contractEmbed, category.DiscordCategory, coop);
                }
                if (headerChannel == null) continue;
                try {
                    return (await CreateThreadChannelAsync(coop.Name, headerChannel), headerChannel);
                }
                catch (Exception) { }
			}
            
            if(completedCoops is null || completedCoops.Count == 0) {
                return (null, null);
            }

            var completedCoop = completedCoops.First();
            completedCoops.Remove(completedCoop);
            var completedCoopParentChannel = (_client.GetChannel(completedCoop.ThreadParentChannel) as SocketTextChannel);
			var coopThread = completedCoopParentChannel.Threads.FirstOrDefault(t => t.IsLocked && !t.IsArchived && t.Id == completedCoop.ThreadID);
            if(coopThread != null) {
                try {
                    await coopThread.ModifyAsync(a => a.Archived = true);
				} catch (Exception) {
					await coopThread.ModifyAsync(a => a.Archived = true);
				}
				coopThread = completedCoopParentChannel.Threads.FirstOrDefault(t => !t.IsArchived && t.Id == completedCoop.ThreadID);
                if(coopThread == null){
					var server = servers.Where(x => x.Guild.Id == completedCoop.GuildId).FirstOrDefault();
					return (await CreateThreadChannelAsync(coop.Name, headerChannel), headerChannel);
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
                    return (ServerFunction == ServerFunction.Primary ? 450 : 495) - Guild.GetInUseChannelCount();
                }
            }
            public int ThreadsLeft {
                get {
                    if (Guild == null) return 0;
                    return (ServerFunction == ServerFunction.Primary ? 900 : 995) - Guild.GetInUseThreadCount();
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
