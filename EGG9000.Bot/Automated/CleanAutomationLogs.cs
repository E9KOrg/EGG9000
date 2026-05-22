using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class CleanAutomationLogs(IServiceProvider provider) : _UpdaterBase<CleanAutomationLogs>(TimeSpan.FromHours(12), TimeSpan.FromMinutes(5), provider) {
        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
            var deleted = await _db.AutomationLogs.Where(x => x.StartTime < cutoff).ExecuteDeleteAsync(cancellationToken);
            _logger.LogInformation("Deleted {count} AutomationLogs older than {cutoff}", deleted, cutoff);
        }
    }
}
