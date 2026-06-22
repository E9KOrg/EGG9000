using EGG9000.Common.Database.Entities;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
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
            _cachedUsers = db.DBUsers.AsNoTracking().ToList();

            _cachedActiveCoops = db.Coops.AsNoTracking().Include(x => x.Contract).Where(c => !c.Finished && !c.DeletedChannel && !c.ThreadArchived).ToList();
        }

        private volatile List<DBUser> _cachedUsers;
        private DateTimeOffset _lastCacheUpdateUser;

        public List<DBUser> GetCachedUsers() => _cachedUsers;
        public Task<List<DBUser>> GetDbUsers() => Task.FromResult(_cachedUsers);

        // A user counts as changed since the last refresh when its LastModified moved past the
        // cutoff. CreateOn is intentionally not tested: the Added hook (ApplicationDbContext) stamps
        // LastModified at insert time and CreateOn never changes afterward, so LastModified >= CreateOn
        // always holds and an "OR CreateOn > cutoff" branch can never match a row LastModified misses.
        // Keeping the predicate single-column lets Postgres use IX_Users_LastModified instead of
        // seq-scanning Users every refresh. Equivalence is covered by DatabaseCacheFilterTests.
        public static Expression<Func<DBUser, bool>> UpdatedSince(DateTimeOffset cutoff)
            => u => u.LastModified > cutoff;

        public async Task RefreshUserCache() {
            try {
                var db = await _dbContextFactory.CreateDbContextAsync();
                var currentCacheTime = _lastCacheUpdateUser;
                _lastCacheUpdateUser = DateTimeOffset.UtcNow;
                var updatedUsers = await db.DBUsers.AsNoTracking().Where(UpdatedSince(currentCacheTime)).ToListAsync();
                _logger.LogInformation("Refreshing DBUser cache, found {Count} updated users. (Last cache update {LastCacheUpdate})", updatedUsers.Count, (_lastCacheUpdateUser - currentCacheTime).Humanize());
                var newList = new List<DBUser>(_cachedUsers);
                newList.RemoveAll(u => updatedUsers.Any(uu => uu.Id == u.Id));
                newList.AddRange(updatedUsers);
                _cachedUsers = newList;
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
                var coops = await db.Coops.AsNoTracking().Include(x => x.Contract).Where(c => !c.Finished && !c.DeletedChannel && !c.ThreadArchived).ToListAsync();
                _cachedActiveCoops = coops;
            } catch(Exception e) {
                _logger.LogError(e, "Error refreshing active coops cache");
            }
        }
    }
}
