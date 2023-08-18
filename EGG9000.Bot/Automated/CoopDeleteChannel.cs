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
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Automated {
    public class CoopDeleteChannel : _UpdaterBase<CoopDeleteChannel> {

        public CoopDeleteChannel(
            IServiceProvider provider
        ) : base(TimeSpan.FromMinutes(10), TimeSpan.Zero, provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coops = await _db.Coops.AsQueryable().Where(x => x.CoopEnds.HasValue && x.CoopEnds.Value.AddDays(3) < DateTimeOffset.Now && !x.DeletedChannel).ToListAsync();
            //var coops = await _db.Coops.AsQueryable().Where(x => x.CoopEnds.HasValue && x.CoopEnds.Value.AddDays(1) < DateTimeOffset.Now && !x.DeletedChannel).ToListAsync();


            coops.AddRange(await _db.Coops.AsQueryable().Where(x => (x.Finished || x.FinishedOrFailed) && !x.DeletedChannel && (x.CoopCompleted == null || x.CoopCompleted < DateTimeOffset.Now.AddDays(-2))).ToListAsync());
            //coops.AddRange(await _db.Coops.AsQueryable().Where(x => x.Finished && !x.DeletedChannel && (x.CoopCompleted == null || x.CoopCompleted < DateTimeOffset.Now.AddHours(-12))).ToListAsync());


            foreach(var coop in coops) {
                var coopChannel = (ITextChannel)_client.GetChannel(coop.DiscordChannelId);
                if(coopChannel == null) {
                    coopChannel = (ITextChannel)(await _client.Rest.GetChannelAsync(coop.DiscordChannelId));
                }
                if(coopChannel != null) {
                    try {
                        await coopChannel.DeleteAsync();
                        _logger.LogInformation("Deleting co-op channel for {coopName}", coop.Name);
                        coop.DeletedChannel = true;
                    } catch(Exception e) {
                        _bugsnag.Notify(e);
                        _logger.LogError("Error deleting co-op channel for {coopName}: {error}", coop.Name, e.Message);
                    }
                } else {
                    coop.DeletedChannel = true;
                    _logger.LogWarning("Unable to find co-op channel for {coopName}", coop.Name);
                }
                try {
                    await _db.SaveChangesAsync();
                } catch(Exception) {
                    await _db.SaveChangesAsync();
                }
            }


        }
    }
}
