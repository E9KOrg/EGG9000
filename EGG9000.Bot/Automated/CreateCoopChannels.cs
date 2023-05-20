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

namespace EGG9000.Bot.Automated {
    public class CreateCoopChannels : _UpdaterBase<CreateCoopChannels> {
        public CreateCoopChannels(
            IServiceProvider provider
        ) : base(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(0), provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            ApplicationDbContext _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coops = await _db.Coops.AsQueryable().Where(x => x.DiscordChannelId == 0 && !x.DeletedChannel).ToListAsync();

            if(coops.Count > 0) {
                foreach(var coopGroups in coops.GroupBy(x => x.GuildId)) {
                    var guild = _client.Guilds.First(x => x.Id == coopGroups.Key);
                    var servers = await GetOverflowGuildsCounts(guild, _db);
                    if(servers == null) {
                        var dbguild = await _db.Guilds.Where(x => x.OverflowServersJson.Contains(coopGroups.Key.ToString())).FirstOrDefaultAsync();
                        if(dbguild == null) {
                            _logger.LogWarning("Co-op is trying to be made for guild that is not registered, {guildname} {guildid}, Co-op Name {coop}, Users {user}",
                                guild.Name, guild.Id, coopGroups.First().Name,
                                string.Join(", ", await _db.UserCoopXrefs.Where(x => x.CoopId == coopGroups.First().Id).Select(x => x.User.DiscordUsername).ToListAsync())
                            );
                            continue;
                        }
                        guild = _client.Guilds.First(X => X.Id == dbguild.Id);
                        servers = await GetOverflowGuildsCounts(guild, _db);
                        foreach(var coopGroup in coopGroups) {
                            coopGroup.GuildId = dbguild.Id;
                        }
                    }
                    var completedCoops = await _db.Coops.AsQueryable().Where(x => !x.DeletedChannel && x.Status == CoopStatusEnum.Completed).OrderBy(x => x.CoopCompleted).ToListAsync();
                    _logger.LogInformation("Coop Counts {count} {guild}", coopGroups.Count(), guild.Name);
                    foreach(var coop in coopGroups) {
                        if(cancellationToken.IsCancellationRequested) return;

                        try {
                            var channel = await CreateTextChannelAsync(guild, coop, servers, completedCoops, cancellationToken);
                            if(channel != null) {
                                coop.DiscordChannelId = channel.Id;
                                coop.OverflowGuildId = channel.GuildId;

                                _logger.LogInformation("Channel created for {coopName}", coop.Name);
                                try {
                                    await _db.SaveChangesAsync();
                                } catch(Exception) {
                                    await _db.SaveChangesAsync();
                                }
                            } else {
                                _logger.LogWarning("Channel NOT created for {coopName}", coop.Name);
                            }
                        } catch(Exception ex) {
                            _logger.LogError(ex, "Error Creating Co-op Channel {coop} in {guild}", coop.Name, guild.Name);
                        }
                    }
                }

            }
        }

        private async Task<ITextChannel> CreateTextChannelAsync(SocketGuild guild, Coop coop, List<OverflowServer> servers, List<Coop> completedCoops, CancellationToken cancellationToken) {
            foreach(var overflow in servers.Where(x => x.ChannelsLeft > 0)) {
                var coopCategories = await overflow.GetCoopCategories(_client);
                foreach(var category in coopCategories.Where(x => x.CurrentCount < 50)) {
                    try {
                        var channel = await overflow.Guild.CreateTextChannelAsync(coop.Name, x => { x.CategoryId = category.DiscordCategory.Id; }, options: new RequestOptions { CancelToken = cancellationToken });
                        category.CurrentCount++;
                        overflow.ChannelsLeft--;
                        return channel;
                    } catch(Exception) { }
                }
            }
            if(completedCoops.Count() > 0) {
                var completedCoop = completedCoops.First();
                completedCoops.Remove(completedCoop);
                var coopChannel = (ITextChannel)_client.GetChannel(coop.DiscordChannelId);
                if(coopChannel == null) {
                    coopChannel = (ITextChannel)(await _client.Rest.GetChannelAsync(coop.DiscordChannelId, options: new RequestOptions { CancelToken = cancellationToken }));
                }
                if(coopChannel != null) {
                    try {
                        await coopChannel.DeleteAsync();
                    } catch(Exception) {

                    }
                    coop.DeletedChannel = true;
                    _logger.LogInformation("Deleting co-op channel for {coop} to free up a channel", completedCoop.Name);

                    var server = servers.Where(x => x.Guild.Id == completedCoop.GuildId).FirstOrDefault();
                    if(server != null) {
                        var coopCategories = await server.GetCoopCategories(_client);
                        foreach(var category in coopCategories.Where(x => x.CurrentCount < 50)) {
                            try {
                                var channel = await server.Guild.CreateTextChannelAsync(coop.Name, x => { x.CategoryId = category.DiscordCategory.Id; }, options: new RequestOptions { CancelToken = cancellationToken });
                                category.CurrentCount++;
                                return channel;
                            } catch(Exception) { }
                        }
                    }
                } else {
                    coop.DeletedChannel = true;
                    _logger.LogWarning("Unable to find co-op channel for {coop} to be able to free up space, settings as deleted", completedCoop.Name);
                    completedCoops.Remove(completedCoop);
                    return await CreateTextChannelAsync(guild, coop, servers, completedCoops, cancellationToken);
                }

            }
            return null;
        }


        private async Task<List<OverflowServer>> GetOverflowGuildsCounts(SocketGuild guild, ApplicationDbContext db) {
            var servers = new List<OverflowServer>();
            servers.Add(new OverflowServer(guild) { ChannelsLeft = 500 - guild.Channels.Count - 50 });

            var dbguild = await db.Guilds.AsAsyncEnumerable().FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
            if(dbguild == null) {
                return null;
            }
            foreach(var overflow in dbguild.OverflowServers) {
                var overflowGuild = _client.Guilds.First(x => x.Id == overflow);
                servers.Add(new OverflowServer(overflowGuild) { ChannelsLeft = 500 - overflowGuild.Channels.Count });
            }
            return servers;
        }

        public class OverflowServer {
            public SocketGuild Guild { get; set; }
            public int ChannelsLeft { get; set; }
            private List<CoopCategories> CoopCategories { get; set; }

            public async Task<List<CoopCategories>> GetCoopCategories(DiscordHostedService discord) {
                if(CoopCategories == null)
                    CoopCategories = (await discord.GetAllCoopCategories(Guild)).Select(x => new CoopCategories { DiscordCategory = x, CurrentCount = Guild.TextChannels.Count(y => y.CategoryId == x.Id) }).ToList();
                return CoopCategories;
            }
            public OverflowServer(SocketGuild guild) {
                Guild = guild;
            }
        }

        public class CoopCategories {
            public SocketGuildChannel DiscordCategory { get; set; }
            public int CurrentCount { get; set; }
        }
    }
}
