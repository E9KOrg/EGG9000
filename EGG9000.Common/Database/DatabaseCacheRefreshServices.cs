using EGG9000.Common.Services;

using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    public class UserCacheRefreshService(DatabaseCache cache, ILogger<UserCacheRefreshService> logger)
        : PeriodicBackgroundService(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), logger) {
        protected override Task DoWorkAsync(CancellationToken cancellationToken) => cache.RefreshUserCache();
    }

    public class ActiveCoopsCacheRefreshService(DatabaseCache cache, ILogger<ActiveCoopsCacheRefreshService> logger)
        : PeriodicBackgroundService(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), logger) {
        protected override Task DoWorkAsync(CancellationToken cancellationToken) => cache.RefreshActiveCoopsCache();
    }
}
