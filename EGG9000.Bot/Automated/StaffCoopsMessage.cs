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
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Bot.Automated {
    public class StaffCoopsMessage : _UpdaterBase<StaffCoopsMessage> {

        public StaffCoopsMessage(
            IServiceProvider provider
        ) : base(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(0), provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbguilds = await _db.Guilds.ToListAsync();

            foreach(var dbguild in dbguilds.Where(x => x.StaffCoopsMessageDetails?.StartsWith("{") ?? false)) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                await guild.DownloadUsersAsync();
                var guildInfo = JsonConvert.DeserializeObject<GuildInfo>(dbguild.StaffCoopsMessageDetails);
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

            await CleanUpTalkToEgg9000(_db);
        }

        public async Task CleanUpTalkToEgg9000(ApplicationDbContext db) {
            var channel = (SocketTextChannel)_client.GetChannel(799084354638446649);
            if(channel is null)
                return;
            var messages = await channel.GetMessagesAsync(limit: 1000).FlattenAsync();
            var messagesToDelete = messages.Where(x => x.Content.Contains("used the command `/nextrank") && x.CreatedAt < DateTimeOffset.Now.AddHours(-1) || x.Content.ToLower().Trim() == "/nextrank");
            await channel.DeleteMessagesBatchAsync(messagesToDelete);
        }

        private class GuildInfo {
            public List<ulong> Roles { get; set; }
            public ulong ChannelID { get; set; }
            public ulong MessageID { get; set; }
        }
    }
}
