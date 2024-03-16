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

namespace EGG9000.Bot.Automated.Coops {
    public class CreateCoopChannels(IServiceProvider provider) 
            : _UpdaterBase<CreateCoopChannels>(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coops = await _db.Coops.AsQueryable().Where(x => x.DiscordChannelId == 0 && !x.DeletedChannel).ToListAsync(cancellationToken);

            if(coops is null || coops.Count == 0) {
                return;
            }

            foreach(var coopGroups in coops.GroupBy(x => x.GuildId)) {
                var guild = _client.Guilds.First(x => x.Id == coopGroups.Key);
                var servers = await GetOverflowGuildsCounts(guild, _db);
                if(servers == null) {
                    var dbguild = await _db.Guilds.Where(x => x.OverflowServersJson.Contains(coopGroups.Key.ToString())).FirstOrDefaultAsync(cancellationToken);
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
                    if(cancellationToken.IsCancellationRequested) return;

                    try {
                        var channel = await TryCreateCoopChannel(guild, coop, servers, completedCoops, cancellationToken);
                        if(channel != null) {
                            coop.DiscordChannelId = channel.Id;
                            coop.OverflowGuildId = channel.GuildId;

                            _logger.LogInformation("Channel created for {coopName}", coop.Name);
                            try {
                                await _db.SaveChangesAsync(cancellationToken);
                            } catch(Exception) {
                                await _db.SaveChangesAsync(cancellationToken);
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

        private async Task<ITextChannel> CreateTextChannelAsync(SocketGuild guild, string channelName, SocketGuildChannel category, CancellationToken cancellationToken) {
            try {
                return await guild.CreateTextChannelAsync(
                    channelName,
                    x => { x.CategoryId = category.Id; },
                    options: new RequestOptions { CancelToken = cancellationToken, RetryMode = RetryMode.RetryTimeouts }
                );
            } catch(HttpException dException) {
                if(dException.DiscordCode == DiscordErrorCode.MaximumGuildChannelsReached) {
                    //This is to be somewhat expected, the 500 limit is a bit awful
                } else if(dException.DiscordCode == DiscordErrorCode.InvalidFormBody &&
                    dException.Errors.Any(e => e.Errors.Any(ei => ei.Code.ToUpper() == "CHANNEL_PARENT_MAX_CHANNELS"))) {
                    //This is what we care about
                    _logger.LogError(
                        "Channel for coop {coop} not created. Already 50 channels in category {category} in server {server}.",
                       channelName, category.Name, guild.Name
                    );
                }
            } catch(Exception) { }
            return null;
        }

        private async Task<ITextChannel> TryCreateCoopChannel(SocketGuild guild, Coop coop, List<OverflowServer> servers, List<Coop> completedCoops, CancellationToken cancellationToken) {
            foreach(var overflow in servers.Where(x => x.ChannelsLeft > 0)) {
                var coopCategories = await overflow.GetCoopCategories(_client);
                foreach(var category in coopCategories.Where(x => x.CurrentCount < 50)) {
                    try {
                        return await CreateTextChannelAsync(overflow.Guild, coop.Name, category.DiscordCategory, cancellationToken);
                    } catch(Exception) {}
                }
            }
            if(completedCoops is null || completedCoops.Count == 0) {
                return null;
            }

            var completedCoop = completedCoops.First();
            completedCoops.Remove(completedCoop);
            var coopChannel = (ITextChannel)_client.GetChannel(completedCoop.DiscordChannelId);
            if(coopChannel == null && completedCoop.DiscordChannelId > 0) {
                coopChannel = (ITextChannel)await _client.Rest.GetChannelAsync(completedCoop.DiscordChannelId, options: new RequestOptions { CancelToken = cancellationToken });
            }
            if(coopChannel != null) {
                try {
                    await coopChannel.DeleteAsync(options: new RequestOptions { CancelToken = cancellationToken, RetryMode = RetryMode.RetryTimeouts });
                } catch(Exception) { }

                coopChannel = (ITextChannel)await _client.Rest.GetChannelAsync(completedCoop.DiscordChannelId, options: new RequestOptions { CancelToken = cancellationToken });
                if(coopChannel == null) {
                    completedCoop.DeletedChannel = true;
                    _logger.LogInformation("Deleted co-op channel for {coop} to free up a channel", completedCoop.Name);

                    var server = servers.Where(x => x.Guild.Id == completedCoop.GuildId).FirstOrDefault();
                    if(server != null) {
                        var coopCategories = await server.GetCoopCategories(_client);
                        foreach(var category in coopCategories.Where(x => x.CurrentCount < 50)) {
                            try {
                                return await CreateTextChannelAsync(server.Guild, coop.Name, category.DiscordCategory, cancellationToken);
                            } catch(Exception) { }
                        }
                    }
                } else {
                    _logger.LogWarning("Unable to delete co-op channel for {coop} - was not able to free up space, re-iterating", completedCoop.Name);
                    return await TryCreateCoopChannel(guild, coop, servers, completedCoops, cancellationToken);
                }
            } else {
                _logger.LogWarning("Unable to find co-op channel for {coop} to be able to free up space, setting as deleted", completedCoop.Name);
                completedCoops.Remove(completedCoop);
                return await TryCreateCoopChannel(guild, coop, servers, completedCoops, cancellationToken);
            }

            return null;
        }


        private static async Task<List<OverflowServer>> GetOverflowGuildsCounts(SocketGuild guild, ApplicationDbContext db) {
            var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
            if(dbguild == null) { return null; }
            return [
                new(guild),
                .. dbguild.OverflowServers.Select(os => new OverflowServer(ServerFunction.Overflow){ Guild = })
            ];
        }

        public enum ServerFunction { Primary = 0, Overflow = 1};
        public class OverflowServer(ServerFunction serverFunction = ServerFunction.Primary) {
            public SocketGuild Guild { get; set; }
            private readonly ServerFunction ServerFunction = serverFunction;
            public int ChannelsLeft { 
                get {
                    if(Guild == null) return 0;
                    return (ServerFunction == ServerFunction.Primary ? 450 : 500) - Guild.GetEffectiveChannelCount();
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
                    return Guild.GetEffectiveChannelCount(DiscordCategory);
                } 
            }
        }
    }
}
