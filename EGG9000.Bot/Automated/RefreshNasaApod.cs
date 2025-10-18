using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using MassTransit.Initializers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
     public class RefreshNasaApod(IServiceProvider provider) : _UpdaterBase<RefreshNasaApod>(TimeSpan.FromMinutes(15), TimeSpan.Zero, provider) {
#nullable enable
        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var fetchedNew = await FetchNewAPOD(_db, cancellationToken);
            if (_latestApodCache is null) {
                _logger.LogInformation("No cached APOD after fetching new, skipping rest of Job.");
                return;
            }
            var latestPostId = _latestApodCache!.ID;

            var enabledGuilds = await _db.Guilds.Where(g => g.HasChannel(GuildChannelType.NasaApodChannel)).Select(g => new {
                Guild = g,
                Cache = _db.GetNasaApodCache(g).Result
            }).ToListAsync(cancellationToken);
            var outOfDateGuilds = enabledGuilds.Where(g => g.Cache.LastApodPostedId != latestPostId);
        }

        private async Task<bool> FetchNewAPOD(ApplicationDbContext _db, CancellationToken cancellationToken) {
            var latestApod = await NasaHelper.GetNasaApodResponseAsync(_logger, cancellationToken);
            if(latestApod is null) {
                _logger.LogWarning("Failed to fetch latest APOD.");
                return false;
            } else if(_latestApodCache is not null && latestApod.ID == _latestApodCache.ID) {
                _logger.LogInformation("No new APOD found.");
                return false;
            }
            
            var existingApod = await GetLatestApod(_db);
            if(existingApod is not null && latestApod.ID == existingApod.ID) {
                _logger.LogInformation("No new APOD found against database.");
                _latestApodCache = existingApod;
                return false;
            }

            _logger.LogInformation("New APOD found: {Title} ({Date})", latestApod.Title, latestApod.DateString);
            await _db.NasaApods.AddAsync(latestApod, cancellationToken);
            return true;
        } 

        private async Task<NasaApod?> GetLatestApod(ApplicationDbContext _db) {
            _latestApodCache ??= await _db.NasaApods.OrderByDescending(a => a.Date).FirstOrDefaultAsync();
            return _latestApodCache;
        }

        private NasaApod? _latestApodCache = null;
    }
}
