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

namespace EGG9000.Bot.Automated {
    public class CreateCoopChannels : _UpdaterBase<CreateCoopChannels> {
        public CreateCoopChannels(
            IServiceProvider provider
        ) : base(TimeSpan.FromSeconds(10), TimeSpan.Zero, provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            ApplicationDbContext _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
            var coops = await _db.Coops.AsQueryable().Where(x => x.DiscordChannelId == 0 && !x.DeletedChannel).ToListAsync();

            if(coops.Count > 0) {
                foreach(var coopGroups in coops.GroupBy(x => x.GuildId)) {
                    var guild = _client.Guilds.First(x => x.Id == coopGroups.Key);
                    var servers = await GetOverflowGuildsCounts(guild, _db);
                    var completedCoops = await _db.Coops.AsQueryable().Where(x => !x.DeletedChannel && x.Status == CoopStatusEnum.Completed).OrderBy(x => x.CoopCompleted).ToListAsync();
                    Console.WriteLine($"Coop Counts {coopGroups.Count()} {coopGroups.Key}");
                    foreach(var coop in coopGroups) {
                        if(cancellationToken.IsCancellationRequested) return;
                        

                        Console.WriteLine($"Creating Channel for {coop.Name}");
                        var channel = await CreateTextChannelAsync(guild, coop, servers, completedCoops, cancellationToken);
                        if(channel != null) {
                            coop.DiscordChannelId = channel.Id;
                            coop.OverflowGuildId = channel.GuildId;

                            Console.WriteLine($"Channel created for {coop.Name}");
                            try {
                                await _db.SaveChangesAsync();
                            } catch(Exception) {
                                await _db.SaveChangesAsync();
                            }
                        } else {
                            Console.WriteLine($"Channel NOT created for {coop.Name}");
                        }
                    }
                }

            }
        }

        private async Task<ITextChannel> CreateTextChannelAsync(SocketGuild guild, Coop coop, List<OverflowServer> servers, List<Coop> completedCoops, CancellationToken cancellationToken) {
            Console.WriteLine("Checking servers");
            foreach(var overflow in servers.Where(x => x.ChannelsLeft > 0)) {
                Console.WriteLine($"Getting categories for {overflow.Guild.Name}");
                var coopCategories = await overflow.GetCoopCategories(_client);
                foreach(var category in coopCategories.Where(x => x.CurrentCount < 50)) {
                    Console.WriteLine($"Creating channel in category {category.DiscordCategory.Name}");
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
                    Console.WriteLine($"Deleting co-op channel for {coop.Name}");

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
                    Console.WriteLine($"Unable to find co-op channel for {coop.Name}");
                }

            }
            return null;
        }


        private async Task<List<OverflowServer>> GetOverflowGuildsCounts(SocketGuild guild, ApplicationDbContext db) {
            var servers = new List<OverflowServer>();
            servers.Add(new OverflowServer(guild) { ChannelsLeft = 500 - guild.Channels.Count - 50 });

            var dbguild = await db.Guilds.AsAsyncEnumerable().FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
            if(dbguild == null) {
                return servers;
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
