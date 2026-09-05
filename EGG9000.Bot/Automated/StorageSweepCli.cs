using EGG9000.Common.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public static class StorageSweepCli {
        public const string Switch = "--storage-sweep";
        public const string ConnectionSwitch = "--conn";

        public static bool Requested(string[] args) {
            return Array.Exists(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        }

        public static string ConnectionArgument(string[] args) {
            for(var i = 0; i < args.Length - 1; i++) {
                if(string.Equals(args[i], ConnectionSwitch, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        public static async Task<int> RunAsync(string connectionString) {
            using var loggerFactory = LoggerFactory.Create(b => b.ClearProviders().SetMinimumLevel(LogLevel.Information).AddNLog());
            var logger = loggerFactory.CreateLogger<StorageSweep>();
            if(string.IsNullOrWhiteSpace(connectionString)) {
                logger.LogError("storage sweep CLI needs {Switch} \"<npgsql connection string>\"", ConnectionSwitch);
                return 1;
            }

            var services = new ServiceCollection();
            services.AddSingleton(loggerFactory);
            services.AddLogging();
            services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString, x => x.MigrationsAssembly("EGG9000.Common")));
            await using var provider = services.BuildServiceProvider();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            var options = StorageSweepOptions.FromEnvironment() with { Enabled = true };
            var sweep = new StorageSweep(provider.GetRequiredService<IServiceScopeFactory>(), logger);
            await sweep.RunOnceAsync(options, cts.Token);
            NLog.LogManager.Flush();
            return cts.IsCancellationRequested ? 2 : 0;
        }
    }
}
