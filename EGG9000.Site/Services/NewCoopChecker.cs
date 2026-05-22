using EGG9000.Common.Database;
using EGG9000.Common.Services;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Site.Services {
    public class NewCoopChecker(IServiceScopeFactory factory, ILogger<NewCoopChecker> logger)
        : PeriodicBackgroundService(TimeSpan.FromSeconds(30), TimeSpan.Zero, logger) {
        public static bool WaitingOnCoops = false;

        protected override async Task DoWorkAsync(CancellationToken cancellationToken) {
            var sw = Stopwatch.StartNew();
            using var scope = factory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var coopCount = await db.Coops.Where(x => x.Status == Common.Database.Entities.CoopStatusEnum.WaitingOnThread).CountAsync(cancellationToken);
            if(coopCount > 20 && WaitingOnCoops == false)
                WaitingOnCoops = true;
            else if(coopCount < 10 && WaitingOnCoops == true)
                WaitingOnCoops = false;
            sw.Stop();
            _logger.LogInformation("NewCoopChecker Hosted Service is working. Count: {Count}, Time: {sw}", coopCount, TimeSpan.FromTicks(sw.ElapsedTicks).Humanize());
        }
    }
}
