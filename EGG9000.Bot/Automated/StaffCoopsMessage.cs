using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Common.Helpers;
using Ei;
using Humanizer;
using Discord.Rest;

namespace EGG9000.Bot.Automated {
    public class StaffCoopsMessage : _UpdaterBase {
        private IConfiguration Configuration;

        public StaffCoopsMessage(IConfiguration Configuration, DiscordSocketClient client,
            Bugsnag.IClient bugsnag) : base(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5), client, bugsnag) {
            this.Configuration = Configuration;
        }

        public override async Task Run(object state) {

            var guildInfos = new List<GuildInfo> { new GuildInfo {
                GuildID = 656455567858073601,
                Roles = new List<ulong> { 708378160143794177 , 759887156029423636 , 750797304797069323 },
                ChannelID = 809236825880920075,
                MessageID = 846326801076191242
            },
            new GuildInfo {
                GuildID = 847108222644650004,
                Roles = new List<ulong> { 847971934037868554 },
                ChannelID = 849409431791992932,
                MessageID = 849410095069659146
            }
            };

            var _db = new ApplicationDbContext(Configuration["ConnectionStrings:DefaultConnection"]);


            foreach(var guildInfo in guildInfos) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == guildInfo.GuildID);
                await guild.DownloadUsersAsync();
                var admins = guild.Users.Where(x => x.Roles.Any(r => guildInfo.Roles.Contains(r.Id)));

                var adminDiscordIds = admins.Select(x => x.Id);

                var adminUsers = await _db.DBUsers.AsQueryable().Where(x => adminDiscordIds.Contains(x.DiscordId)).ToListAsync();

                var adminUserIds = adminUsers.Select(x => x.Id);
                var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).AsQueryable().Where(x => !x.DeletedChannel && x.UserCoopsXrefs.Any(y => adminUserIds.Contains(y.UserId))).ToListAsync();


                var adminsWithChannels = adminUsers.OrderBy(x => x.DiscordUsername).Select(u => new {
                    Admin = u,
                    Channels = coops.Where(c => c.UserCoopsXrefs.Any(xref => u.Id == xref.UserId)).Select(c => $"<#{c.DiscordChannelId}>")
                });

                var channel = guild.GetTextChannel(guildInfo.ChannelID);
                RestUserMessage message = ((RestUserMessage)await channel.GetMessageAsync(guildInfo.MessageID));

                await message.ModifyAsync(x => x.Content = string.Join("\n", adminsWithChannels.Select(x => $"{x.Admin.DiscordUsername}: {string.Join(", ", x.Channels)}")));
            }
        }

        private class GuildInfo {
            public ulong GuildID { get; set; }
            public List<ulong> Roles { get; set; }
            public ulong ChannelID { get; set; }
            public ulong MessageID { get; set; }
        }
    }
}
