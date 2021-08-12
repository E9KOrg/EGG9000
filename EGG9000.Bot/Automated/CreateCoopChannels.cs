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

namespace EGG9000.Bot.Automated {
    public class CreateCoopChannels : _UpdaterBase {
        private IConfiguration _config;
        private ApplicationDbContext _db;

        public CreateCoopChannels(IConfiguration Configuration, DiscordSocketClient client,
            Bugsnag.IClient bugsnag) : base(TimeSpan.FromMinutes(1), TimeSpan.Zero, client, bugsnag) {
            _db = new ApplicationDbContext(Configuration["ConnectionStrings:DefaultConnection"]);
            _config = Configuration;
            _client = client;
        }

        public override async Task Run(object state) {
                    ApplicationDbContext _db = new ApplicationDbContext(_config["ConnectionStrings:DefaultConnection"]);
                    var coops = await _db.Coops.AsQueryable().Where(x => x.DiscordChannelId == 0 && !x.DeletedChannel).ToListAsync();

                    if(coops.Count > 0) {
                        foreach(var coopGroups in coops.GroupBy(x => x.GuildId)) {
                            var guild = _client.Guilds.First(x => x.Id == coopGroups.Key);
                            var servers = await GetOverflowGuildsCounts(guild, _db);
                            foreach(var coop in coopGroups) {
                                var channel = await CreateTextChannelAsync(guild, coop, servers);
                                if(channel != null) {
                                    coop.DiscordChannelId = channel.Id;
                                    coop.OverflowGuildId = channel.GuildId;
                                }
                            }
                        }

                        try {
                            await _db.SaveChangesAsync();
                        } catch(Exception) {
                            await _db.SaveChangesAsync();
                        }
                    }
        }

        private async Task<ITextChannel> CreateTextChannelAsync(SocketGuild guild, Coop coop, List<OverflowServer> servers) {
            foreach(var overflow in servers.Where(x => x.ChannelsLeft > 0)) {
                foreach(var category in overflow.CoopCategories.Where(x => x.CurrentCount < 50)) {
                    try {
                        var channel = await overflow.Guild.CreateTextChannelAsync(coop.Name, x => { x.CategoryId = category.DiscordCategory.Id; });
                        category.CurrentCount++;
                        overflow.ChannelsLeft--;
                        return channel;
                    }catch(Exception) { }
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
                servers.Add(new OverflowServer(overflowGuild) { ChannelsLeft = 500 - overflowGuild.Channels.Count - 5 });
            }
            return servers;
        }

        public class OverflowServer {
            public SocketGuild Guild { get; set; }
            public int ChannelsLeft { get; set; }
            public List<CoopCategories> CoopCategories { get; set; }

            public OverflowServer(SocketGuild guild) {
                Guild = guild;
                CoopCategories = guild.GetCoopCategories().Select(x => new CoopCategories { DiscordCategory = x, CurrentCount = guild.TextChannels.Count(y => y.CategoryId == x.Id) }).ToList();
            }
        }

        public class CoopCategories {
            public SocketGuildChannel DiscordCategory { get; set; }
            public int CurrentCount { get; set; }
        }
    }
}
