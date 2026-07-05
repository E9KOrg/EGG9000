using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class CleanApiKeyRequestLogs(IServiceProvider provider) : _UpdaterBase<CleanApiKeyRequestLogs>(TimeSpan.FromHours(12), TimeSpan.FromMinutes(5), provider) {
        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var logCutoff = DateTimeOffset.UtcNow.AddDays(-7);
            var deletedLogs = await _db.ApiKeyRequestLogs.Where(x => x.Timestamp < logCutoff).ExecuteDeleteAsync(cancellationToken);

            var usageCutoff = DateTimeOffset.UtcNow.AddDays(-90).UtcDateTime.Date;
            var deletedUsage = await _db.ApiKeyDailyUsages.Where(x => x.Date < usageCutoff).ExecuteDeleteAsync(cancellationToken);

            _logger.LogInformation("Deleted {logCount} ApiKeyRequestLogs older than {logCutoff} and {usageCount} ApiKeyDailyUsages older than {usageCutoff}", deletedLogs, logCutoff, deletedUsage, usageCutoff);
        }
    }
}
