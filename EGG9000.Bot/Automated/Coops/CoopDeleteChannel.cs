using Discord;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated.Coops {
    public class CoopDeleteChannel(IServiceProvider provider) : _UpdaterBase<CoopDeleteChannel>(TimeSpan.FromMinutes(10), TimeSpan.Zero, provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coops = await _db.Coops.AsQueryable().Where(x => x.ThreadID == 0 && x.CoopEnds.HasValue && x.CoopEnds.Value.AddDays(3) < DateTimeOffset.Now && !x.DeletedChannel).ToListAsync(cancellationToken);

            coops.AddRange(await _db.Coops.AsQueryable().Where(x => x.ThreadID == 0 && ( x.Finished || x.Status == CoopStatusEnum.Failed) && !x.DeletedChannel && (x.CoopCompleted == null || x.CoopCompleted < DateTimeOffset.Now.AddDays(-2))).ToListAsync(cancellationToken));

            foreach(var coop in coops) {
                var coopChannel = (ITextChannel)_client.GetChannel(coop.DiscordChannelId);
                coopChannel ??= (ITextChannel)await _client.Rest.GetChannelAsync(coop.DiscordChannelId);
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
                    await _db.SaveChangesAsync(cancellationToken);
                } catch(Exception) {
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}