using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.NasaHelper;

namespace EGG9000.Bot.Automated; 
 public class RefreshNasaApod(IServiceProvider provider) : _UpdaterBase<RefreshNasaApod>(TimeSpan.FromMinutes(15), TimeSpan.Zero, provider) {
#nullable enable
    public async override Task Run(object state, CancellationToken cancellationToken) {
        _logger.LogInformation("Starting...");
        var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var dbNeedsUpdate = await NasaHelper.FetchNewAPOD(_db, _logger, cancellationToken);
        var latestPost = await NasaHelper.GetLatestApod(_db);
        if (latestPost is null) {
            _logger.LogWarning("No latest post after refreshing.");
            return;
        }

        var guilds = await _db.Guilds.ToListAsync(cancellationToken);

        var enabledGuilds = new List<(Guild Guild, GuildNasaApodDetails Cache)>();
        foreach(var guild in guilds) {
            enabledGuilds.Add((guild, await _db.GetNasaApodCache(guild)));
        }

        var outOfDateGuilds = enabledGuilds.Where(g => g.Cache.ChannelId != 0 && g.Cache.LastApodPostedId != latestPost.ID);
        foreach(var apodDetails in outOfDateGuilds) {
            if (await apodDetails.Cache.TrySendNasaAPOD(latestPost, _client, _db, _logger)) {
                dbNeedsUpdate = true;
                _logger.LogInformation("Posted APOD {} to Guild {GuildId}", latestPost.DateString, apodDetails.Guild.Id);
            } else _logger.LogWarning("Failed to post APOD to Guild {GuildId}", apodDetails.Guild.Id);
        }
        if(dbNeedsUpdate) await _db.SaveChangesAsyncRetry(2, logger: _logger, cancellationToken: cancellationToken);
    }
}
