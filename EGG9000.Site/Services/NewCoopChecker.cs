using EGG9000.Common.Database;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Site.Services {
    public class NewCoopChecker : IHostedService, IDisposable {
        public static bool WaitingOnCoops = false;
        
        private readonly ILogger<NewCoopChecker> _logger;
        private readonly IServiceScopeFactory _factory;
        private Timer _timer = null;

        public NewCoopChecker(ILogger<NewCoopChecker> logger, IServiceScopeFactory factory) {
            _logger = logger;
            _factory = factory;
            Console.WriteLine("NewCoopChecker started");
        }

        public void Dispose() {
            _timer?.Dispose();
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Timed Hosted Service running.");

            _timer = new Timer(DoWorkAsync, null, TimeSpan.Zero,
                TimeSpan.FromSeconds(30));

            return Task.CompletedTask;
        }

        private async void DoWorkAsync(object state) {
            var sw = new Stopwatch();
            sw.Start();
            using (var scope = _factory.CreateScope()) {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var coopCount = await db.Coops.Where(x => x.Status == Common.Database.Entities.CoopStatusEnum.WaitingOnThread).CountAsync();
                if(coopCount > 20 && WaitingOnCoops == false)
                    WaitingOnCoops = true;
                else if(coopCount < 10 && WaitingOnCoops == true)
                    WaitingOnCoops = false;

                _logger.LogInformation(
                    "NewCoopChecker Hosted Service is working. Count: {Count}, Time: {sw}", coopCount, TimeSpan.FromTicks(sw.ElapsedTicks).Humanize());
                sw.Stop();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("NewCoopChecker Hosted Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }
    }
}
