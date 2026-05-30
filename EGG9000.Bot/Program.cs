using Bugsnag.AspNet.Core;
using Discord.WebSocket;
using EGG9000.Bot;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.Services;
using EGG9000.Common.Consumers;
using EGG9000.Common.Database;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Common.Mocks;
using EGG9000.Common.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;
using System;
using System.Threading;
using System.Threading.Tasks;
using Prometheus;
using System.Reflection;
using System.Collections.Generic;

using Newtonsoft.Json;

// Set up logger before anything else
var logger = LogManager.Setup().GetCurrentClassLogger();
logger.Log(NLog.LogLevel.Info, "Main Start");

string botColor;

try
{
    // Build a minimal configuration to check BOT_ACTIVE before building the full host
    var tempConfig = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .AddUserSecrets<Program>(optional: true) // Optional for Docker scenarios
        .Build();

    var botActive = tempConfig.GetValue("BOT_ACTIVE", true);
    botColor = tempConfig.GetValue<string>("BOT_COLOR") ?? "blue";

#if DEBUG 
    botColor = "debug";
#endif
    NLog.GlobalDiagnosticsContext.Set("CustomMachineName", $"{Environment.MachineName}_{botColor}");
    logger.Log(NLog.LogLevel.Info, "CustomMachineName = " + $"{NLog.GlobalDiagnosticsContext.Get("CustomMachineName")}");

    logger.Log(NLog.LogLevel.Info, "BOT_ACTIVE = " + botActive);
    logger.Log(NLog.LogLevel.Info, "BOT_COLOR = " + botColor);

    if (!botActive)
    {
        logger.Log(NLog.LogLevel.Info, "Bot set to not active. Exiting gracefully without starting services.");
        LogManager.Shutdown();
        return; // Exit cleanly without throwing exception
    }

    // If BOT_ACTIVE is true, proceed with normal host build
    await Host.CreateDefaultBuilder(args)
        .ConfigureLogging(logging => {
            logging.ClearProviders();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
        })
        .UseNLog()
        .ConfigureAppConfiguration((context, config) => {
            config.AddUserSecrets<Program>(optional: true); // Optional for Docker scenarios
        })
        .UseDefaultServiceProvider(options => options.ValidateScopes = false)
        .ConfigureServices(ConfigureServices)
        .Build()
        .RunAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "Fatal error during startup");
    LogManager.Shutdown();
    throw;
}

void ConfigureServices(HostBuilderContext hostContext, IServiceCollection services)
{
    logger.Log(NLog.LogLevel.Info, "ConfigureServices Start");
    try
    {
        var serviceProvider = services.BuildServiceProvider();
        StaticLoggerFactory.Initialize(serviceProvider.GetRequiredService<ILoggerFactory>());

        services.Configure<HostOptions>(options => {
            options.ShutdownTimeout = TimeSpan.FromMinutes(5);
        });

        services.AddMemoryCache();

        services.AddSingleton<DiscordQueueService>();
        services.AddSingleton<IDiscordQueue>(provider => provider.GetRequiredService<DiscordQueueService>());
        services.AddHostedService(provider => provider.GetRequiredService<DiscordQueueService>());
        services.AddSingleton<BotLogger>();

        DockerSecretsHelper.Initialize(hostContext.Configuration);

        // Get connection string - supports both Docker secrets and local development
        var connectionString = DockerSecretsHelper.GetConfigOrSecret(
            hostContext.Configuration,
            "ConnectionStrings:DefaultConnection",
            "db_connection_string");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' not found. " +
                "Ensure it's set in: " +
                "1. Docker secrets (db_connection_string), or " +
                "2. User secrets (dotnet user-secrets set ConnectionStrings:DefaultConnection \"...\"), or " +
                "3. Environment variable (ConnectionStrings__DefaultConnection)");
        }

        logger.Log(NLog.LogLevel.Info, "Using connection string from: " + 
            (DockerSecretsHelper.IsDockerSecretsAvailable() ? "Docker Secrets" : "Configuration/User Secrets"));

        services.AddDbContextFactory<ApplicationDbContext>(options => {
            options.UseNpgsql(connectionString, x => {
                x.MigrationsAssembly("EGG9000.Common");
                x.CommandTimeout(30);
            });
            options.EnableSensitiveDataLogging(true);
        });

#if !DEBUG
        services.Configure<ActiveMonitorOptions>(options => {
            options.ServiceType = "bot";
            options.ColorConfigKey = "BOT_COLOR";
            options.PollIntervalSeconds = 10;
        });
        services.AddSingleton<ActiveMonitorHostedService>();
        services.AddHostedService(provider => provider.GetRequiredService<ActiveMonitorHostedService>());
#endif

        services.AddSingleton<DatabaseCache>();
        services.AddHostedService<UserCacheRefreshService>();
        services.AddHostedService<ActiveCoopsCacheRefreshService>();
        services.AddSingleton<Words>();

#if RELEASE
        var release = true;
#else
        var release = false;
#endif

#if DEBUG


        var serviceCustomize = Type.GetType("EGG9000.Bot.ServiceCustomize");
        if(serviceCustomize is not null && !release) {
            var method = serviceCustomize.GetMethod("ConfigureServices");
            method.Invoke(null, [hostContext, services]);
        }

#else
        if(release) {
            logger.Log(NLog.LogLevel.Info, "RUNNING IN RELEASE");
            
            var bugsnagKey = DockerSecretsHelper.GetConfigOrSecret(
                hostContext.Configuration,
                "ConnectionStrings:BugSnagApiKey",
                "bugsnag_api_key");

            var bugsnagConfig = new Bugsnag.Configuration(bugsnagKey);
            
            var bs = new Bugsnag.Client(bugsnagConfig);

            // Register as singleton for background services
            services.AddSingleton<Bugsnag.IClient>(bs);

            services.AddBugsnag(options => {
                options.ApiKey = bugsnagKey;
                options.AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
                options.ReleaseStage = "production";
            });

            var rabbitmqConn = DockerSecretsHelper.GetConfigOrSecret(
                hostContext.Configuration,
                "ConnectionStrings:RabbitMQServer",
                "rabbitmq_connection");
            
            services.AddOptions<RabbitMqTransportOptions>().Configure(options => {
                var host = rabbitmqConn?.Split("|");
                if(host?.Length > 1) {
                    options.Host = host[0];
                    options.User = host[1];
                    options.Pass = host[2];
                }
            });
            
            services.AddMassTransit(x => {
                x.AddConsumer<ShutdownConsumer>();
                x.AddConsumer<ExpireCacheConsumer>();
                x.AddConsumer<RestartConsumer>();
                if(string.IsNullOrEmpty(rabbitmqConn)) {
                    logger.Log(NLog.LogLevel.Info, "Using RabbitMQ In Memory");
                    x.UsingInMemory((context, cfg) => {
                        cfg.ConfigureEndpoints(context);
                        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                    });
                } else {
                    logger.Log(NLog.LogLevel.Info, "Using RabbitMQ Server");
                    x.UsingRabbitMq((context, cfg) => {
                        cfg.ConfigureEndpoints(context);
                    });
                }
            });
        } else {
            logger.Log(NLog.LogLevel.Info, "RUNNING IN DEBUG");
            services.AddBugsnag();
            services.AddSingleton<IPublishEndpoint>(new PublishEndpointMock());
        }

        services.AddSingleton<DiscordHostedService>();
        services.AddSingleton(provider => provider.GetRequiredService<DiscordHostedService>().Gateway);

        services.Configure<UpdaterOptions<LeaderboardUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(15));
        services.AddHostedService<LeaderboardUpdater>();

        services.AddHostedService<ArtifactCheaters>();
        services.AddHostedService<StaffCoopsMessage>();
        services.AddHostedService<EventUpdater>();

        services.Configure<UpdaterOptions<ThreadsCoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(5));
        services.AddSingleton<ThreadsCoopStatusUpdater>();
        services.AddHostedService(provider => provider.GetService<ThreadsCoopStatusUpdater>());

        services.AddSingleton<ContractUpdater>();
        services.AddHostedService(provider => provider.GetService<ContractUpdater>());

        services.AddHostedService<UserCXPUpdater>();
        services.AddHostedService<NewContracts>();
        services.AddHostedService<CreateCoopViaAPI>();
        services.AddHostedService<CreateCoopThreads>();
        services.AddHostedService<ShipReturnDM>();
        services.AddHostedService<UserSnapShots>();
        services.AddHostedService<ManageOverflow>();
        services.AddHostedService<RemoveTempRoles>();
        services.AddHostedService<HandleGradeChanges>();
        services.AddHostedService<RefreshNasaApod>();
        services.AddHostedService<UpdateBackups>();
        services.AddHostedService<CleanAutomationLogs>();

        services.AddSingleton<CoopsBeingCreatedService>();
        services.AddSingleton<JobService>();
        services.AddHostedService(provider => provider.GetService<JobService>());

        services.AddHostedService<CommandService>();
        services.AddHostedService<DiscordUserService>();


        //services.AddSingleton<IMetricServer>(_ => new MetricServer(port: botColor == "blue" ? 9464 : 9465));
        //services.AddHostedService<PrometheusMetricServerHostedService>();
#endif
    } catch(Exception e) {
        logger.Error(e, "Stopped program because of exception");
        throw;
    }
    finally {
        LogManager.Shutdown();
    }
}

internal class PassiveHostedService : IHostedService {
    readonly ILogger<PassiveHostedService> _logger;
    public PassiveHostedService(ILogger<PassiveHostedService> logger) => _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("PassiveHostedService started. Instance is passive and will not process messages.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("PassiveHostedService stopping.");
        return Task.CompletedTask;
    }
}