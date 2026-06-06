using Discord.Rest;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class StaffCoopsMessage(IServiceProvider provider) : _UpdaterBase<StaffCoopsMessage>(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbguilds = await _db.Guilds.ToListAsync(CancellationToken.None);

            foreach(var dbguild in dbguilds.Where(x => x.StaffCoopsMessageDetails?.StartsWith("{") ?? false)) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                await guild.DownloadUsersAsync();
                var guildInfo = JsonConvert.DeserializeObject<GuildInfo>(dbguild.StaffCoopsMessageDetails);
                var admins = guild.Users.Where(x => x.Roles.Any(r => guildInfo.Roles.Contains(r.Id)));

                var adminDiscordIds = admins.Select(x => x.Id);

                var adminUsers = await _db.DBUsers.AsQueryable().Where(x => adminDiscordIds.Contains(x.DiscordId)).ToListAsync(CancellationToken.None);

                var adminUserIds = adminUsers.Select(x => x.Id);
                var sevenDaysAgo = DateTimeOffset.Now.AddDays(-7);
                // Only ThreadID + member ids are needed, so project them instead of materializing each coop's GZip status blob.
                var coops = await _db.Coops.Where(x => (x.ThreadID != 0 && !x.ThreadArchived) && x.CoopEnds > sevenDaysAgo && x.UserCoopsXrefs.Any(y => adminUserIds.Contains(y.UserId)))
                    .Select(x => new { x.ThreadID, UserIds = x.UserCoopsXrefs.Select(y => y.UserId).ToList() })
                    .ToListAsync(CancellationToken.None);


                var adminsWithChannels = adminUsers.OrderBy(x => x.DiscordUsername).Select(u => new {
                    Admin = u,
                    Channels = coops.Where(c => c.UserIds.Contains(u.Id)).Select(c => $"<#{c.ThreadID}>")
                });

                var channel = guild.GetTextChannel(guildInfo.ChannelID);
                var message = (RestUserMessage)await channel.GetMessageAsync(guildInfo.MessageID);

                var capturedContent = string.Join("\n", adminsWithChannels.Select(x => $"{x.Admin.DiscordUsername}: {string.Join(", ", x.Channels)}"));
                var capturedMessage = message;
                _queue.EnqueueLow(() => capturedMessage.ModifyAsync(x => x.Content = capturedContent));
            }
        }

        private class GuildInfo {
            public List<ulong> Roles { get; set; }
            public ulong ChannelID { get; set; }
            public ulong MessageID { get; set; }
        }
    }
}
