using EGG9000.Common.Database.Entities;

using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    public class DatabaseCache {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<DatabaseCache> _logger;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public DatabaseCache(ApplicationDbContext db, IDbContextFactory<ApplicationDbContext> dbContextFactory, ILogger<DatabaseCache> logger) {
            _db = db;
            _logger = logger;
            _dbContextFactory = dbContextFactory;

            _semaphoreUser.Wait();
            try {
                _lastCacheUpdateUser = DateTimeOffset.UtcNow;
                _cachedUsers = _db.DBUsers.ToList();
            } finally {
                _semaphoreUser.Release();
            }


            RefreshActiveCoopsCache().Wait();
            _timerActiveCoops = new Timer(async _ => await RefreshActiveCoopsCache(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        private List<DBUser> _cachedUsers;
        private DateTimeOffset _lastCacheUpdateUser;
        private readonly SemaphoreSlim _semaphoreUser = new SemaphoreSlim(1, 1);
        public async Task<List<DBUser>> GetDbUsers() {
            await _semaphoreUser.WaitAsync();
            try {
                var currentCacheTime = _lastCacheUpdateUser;
                _lastCacheUpdateUser = DateTimeOffset.UtcNow;
                var updatedUsers = await _db.DBUsers.Where(u => u.LastModified > currentCacheTime || u.CreateOn > currentCacheTime).ToListAsync();
                _cachedUsers.RemoveAll(u => updatedUsers.Any(uu => uu.Id == u.Id));
                _cachedUsers.AddRange(updatedUsers);

                return _cachedUsers;
            } finally {
                _semaphoreUser.Release();
            }
        }

        private List<Coop> _cachedActiveCoops;
        public List<Coop> ActiveCoopsWithFiveMinuteDelay() {
            return _cachedActiveCoops;
        }
        private Timer _timerActiveCoops;
        private async Task RefreshActiveCoopsCache() {
            var db = await _dbContextFactory.CreateDbContextAsync();
            _logger.LogInformation("Refreshing active coops cache");
            var coops = await db.Coops.Include(x => x.Contract).Where(c => !c.Finished && !c.DeletedChannel && !c.ThreadArchived).ToListAsync();
            _cachedActiveCoops = coops;
        }
    }
}
