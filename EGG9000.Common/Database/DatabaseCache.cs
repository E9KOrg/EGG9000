using EGG9000.Common.Database.Entities;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    public class DatabaseCache {
        private readonly ILogger<DatabaseCache> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public DatabaseCache(IDbContextFactory<ApplicationDbContext> dbContextFactory, ILogger<DatabaseCache> logger) {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
            var db = dbContextFactory.CreateDbContext();
            _lastCacheUpdateUser = DateTimeOffset.UtcNow;
            _users = BuildUserSnapshot([.. db.DBUsers.AsNoTracking()]);

            RefreshActiveCoopsCache().Wait();
        }

        // Immutable snapshot swapped atomically on refresh so guild/DiscordId lookups stay consistent with the flat list.
        private sealed class UserSnapshot {
            public required List<DBUser> All { get; init; }
            public required ILookup<ulong, DBUser> ByGuild { get; init; }
            public required Dictionary<ulong, DBUser> ByDiscordId { get; init; }
        }

        private static UserSnapshot BuildUserSnapshot(List<DBUser> users) => new() {
            All = users,
            ByGuild = users.ToLookup(u => u.GuildId),
            ByDiscordId = users.GroupBy(u => u.DiscordId).ToDictionary(g => g.Key, g => g.First())
        };

        private volatile UserSnapshot _users;
        private DateTimeOffset _lastCacheUpdateUser;

        public List<DBUser> GetCachedUsers() => _users.All;
        public Task<List<DBUser>> GetDbUsers() => Task.FromResult(_users.All);

        // Only this guild's users (DBUser.GuildId == the Discord server id). Empty sequence if none cached.
        public IEnumerable<DBUser> GetUsersForGuild(ulong guildId) => _users.ByGuild[guildId];

        // The single cached user with this Discord id, or null.
        public DBUser GetUserByDiscordId(ulong discordId) => _users.ByDiscordId.GetValueOrDefault(discordId);

        public async Task RefreshUserCache() {
            try {
                var db = await _dbContextFactory.CreateDbContextAsync();
                var currentCacheTime = _lastCacheUpdateUser;
                _lastCacheUpdateUser = DateTimeOffset.UtcNow;
                var updatedUsers = await db.DBUsers.AsNoTracking().Where(u => u.LastModified > currentCacheTime || u.CreateOn > currentCacheTime).ToListAsync();
                _logger.LogInformation("Refreshing DBUser cache, found {Count} updated users. (Last cache update {LastCacheUpdate})", updatedUsers.Count, (_lastCacheUpdateUser - currentCacheTime).Humanize());
                var newList = new List<DBUser>(_users.All);
                newList.RemoveAll(u => updatedUsers.Any(uu => uu.Id == u.Id));
                newList.AddRange(updatedUsers);
                _users = BuildUserSnapshot(newList);
            } catch(Exception e) {
                _logger.LogError(e, "Error refreshing user cache");
            }
        }

        private List<Coop> _cachedActiveCoops;
        public List<Coop> ActiveCoopsWithFiveMinuteDelay() {
            return _cachedActiveCoops;
        }
        public async Task RefreshActiveCoopsCache() {
            try {
                var db = await _dbContextFactory.CreateDbContextAsync();
                _logger.LogInformation("Refreshing active coops cache");
                var coops = await db.Coops.AsNoTracking().Include(x => x.Contract).Where(c => !c.Finished && !c.ThreadArchived).ToListAsync();
                _cachedActiveCoops = coops;
            } catch(Exception e) {
                _logger.LogError(e, "Error refreshing active coops cache");
            }
        }
    }
}
