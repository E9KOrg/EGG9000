using EGG9000.Common.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;
using System;

// Set up logger before anything else
var logger = LogManager.Setup().GetCurrentClassLogger();
logger.Log(NLog.LogLevel.Info, "Main Start");

try
{
#if DEBUG
    var machineName = $"{Environment.MachineName}_debug";
#else
    var machineName = Environment.MachineName;
#endif
    NLog.GlobalDiagnosticsContext.Set("CustomMachineName", machineName);
    NLog.GlobalDiagnosticsContext.Set("CustomAppName", "EGG9000.Bot");
    logger.Log(NLog.LogLevel.Info, "CustomMachineName = " + machineName);

    var host = Host.CreateDefaultBuilder(args)
        .ConfigureLogging(logging => {
            logging.ClearProviders();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
        })
        .UseNLog()
        .ConfigureAppConfiguration((context, config) => {
            config.AddUserSecrets<Program>(optional: true); // Optional for Docker scenarios
        })
        .UseDefaultServiceProvider(options => options.ValidateScopes = false)
        .ConfigureServices(EGG9000.Bot.BotHostFactory.ConfigureServices)
        .Build();

#if RELEASE
    // Apply pending migrations on startup. Production only - dev configs (Debug/DEV9001/DEV9002)
    // run against the live DB and must stay manual so a half-written migration can't auto-apply.
    using (var scope = host.Services.CreateScope())
    {
        logger.Log(NLog.LogLevel.Info, "Applying database migrations");
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var migrateCtx = await factory.CreateDbContextAsync();
        await migrateCtx.Database.MigrateAsync();
        logger.Log(NLog.LogLevel.Info, "Database migrations applied");
    }
#endif

    await host.RunAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "Fatal error during startup");
    LogManager.Shutdown();
    throw;
}
