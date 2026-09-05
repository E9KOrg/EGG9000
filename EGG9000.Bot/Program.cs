using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;
using System;
using Sentry;
using System.Threading.Tasks;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var logger = LogManager.Setup().GetCurrentClassLogger();

try {
    var tempConfig = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .AddUserSecrets<Program>(optional: true) // Optional for Docker scenarios
        .Build();
    var botColor = tempConfig.GetValue<string>("BOT_COLOR");
    logger.Log(NLog.LogLevel.Info, $"BotColor = {botColor}");
    var machineName = BuildConfig.IsDebug ? $"{Environment.MachineName}_debug" : !string.IsNullOrEmpty(botColor) ? $"{Environment.MachineName}_{botColor}" : Environment.MachineName;
    GlobalDiagnosticsContext.Set("CustomMachineName", machineName);
    GlobalDiagnosticsContext.Set("CustomAppName", "EGG9000.Bot");
    logger.Log(NLog.LogLevel.Info, "Main Start");

    if(EGG9000.Bot.Automated.StorageSweepCli.Requested(args)) {
        logger.Log(NLog.LogLevel.Info, "Storage sweep CLI mode: no Discord, no caches, explicit connection only");
        return await EGG9000.Bot.Automated.StorageSweepCli.RunAsync(EGG9000.Bot.Automated.StorageSweepCli.ConnectionArgument(args));
    }

    using var sentry = SentrySdk.Init(o =>
    {
        o.Dsn = SecretsHelper.GetConfigOrSecret(
            tempConfig,
            "ConnectionStrings:BugsInkURL",
            "bugsink_url") ?? "";

        o.Environment = BuildConfig.IsRelease ? "Production" : "Development";
        o.AttachStacktrace = true;
        o.TracesSampleRate = 0.0; // no performance tracing
    });

    // Optional: catch truly unhandled process exceptions
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
        logger.Log(NLog.LogLevel.Info, "Manual Exception 1");
        logger.Log(NLog.LogLevel.Error, e.ExceptionObject);
        if(e.ExceptionObject is Exception ex) {
            SentrySdk.CaptureException(ex);
        }
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
        logger.Log(NLog.LogLevel.Info, "Manual Exception 1");
        logger.Log(NLog.LogLevel.Error, e.Exception);
        SentrySdk.CaptureException(e.Exception);
        e.SetObserved();
    };

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

    var release = BuildConfig.IsRelease;
    if(release) {
        // Apply pending migrations on startup. Production only - dev configs (Debug/DEV9001/DEV9002)
        // run against the live DB and must stay manual so a half-written migration can't auto-apply.
        using var scope = host.Services.CreateScope();
        logger.Log(NLog.LogLevel.Info, "Applying database migrations");
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var migrateCtx = await factory.CreateDbContextAsync();
        await migrateCtx.Database.MigrateAsync();
        logger.Log(NLog.LogLevel.Info, "Database migrations applied");
    }

    await host.RunAsync();
    return 0;
} catch(Exception ex) {
    SentrySdk.CaptureException(ex);
    logger.Error(ex, "Fatal error during startup");
    LogManager.Shutdown();
    throw;
}