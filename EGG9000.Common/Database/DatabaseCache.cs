using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    public class DatabaseCache {
        private readonly ApplicationDbContext _db;

        public DatabaseCache(ApplicationDbContext db) {
            _db = db;

            _semaphoreUser.Wait();
            try {
                _lastCacheUpdateUser = DateTimeOffset.UtcNow;
                _cachedUsers = _db.DBUsers.ToList();
            } finally {
                _semaphoreUser.Release();
            }
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
    }
}
