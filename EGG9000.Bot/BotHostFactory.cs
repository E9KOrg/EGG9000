using Bugsnag.AspNet.Core;
using Discord.Interactions;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace EGG9000.Bot;

public static class BotHostFactory {
    public static void ConfigureServices(HostBuilderContext hostContext, IServiceCollection services) {
        var logger = NLog.LogManager.GetCurrentClassLogger();
        logger.Log(NLog.LogLevel.Info, "ConfigureServices Start");
        try {
            var serviceProvider = services.BuildServiceProvider();
            StaticLoggerFactory.Initialize(serviceProvider.GetRequiredService<ILoggerFactory>());

            services.Configure<HostOptions>(options => {
                options.ShutdownTimeout = TimeSpan.FromMinutes(5);
            });

            services.AddMemoryCache();

            services.AddSingleton<DiscordQueueService>();
            services.AddSingleton<IDiscordQueue>(provider => provider.GetRequiredService<DiscordQueueService>());
            services.AddHostedService(provider => provider.GetRequiredService<DiscordQueueService>());

            SecretsHelper.Initialize(hostContext.Configuration);

            var connectionString = SecretsHelper.GetConfigOrSecret(
                hostContext.Configuration,
                "ConnectionStrings:DefaultConnection",
                "db_connection_string");

            if(string.IsNullOrEmpty(connectionString)) {
                throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' not found. " +
                    "Ensure it's set in: " +
                    "1. Docker secrets (db_connection_string), or " +
                    "2. User secrets (dotnet user-secrets set ConnectionStrings:DefaultConnection \"...\"), or " +
                    "3. Environment variable (ConnectionStrings__DefaultConnection)");
            }

            logger.Log(NLog.LogLevel.Info, "Using connection string from: " +
                (SecretsHelper.IsDockerSecretsAvailable() ? "Docker Secrets" : "Configuration/User Secrets"));

            services.AddDbContextFactory<ApplicationDbContext>(options => {
                options.UseNpgsql(connectionString, x => {
                    x.MigrationsAssembly("EGG9000.Common");
                    x.CommandTimeout(30);
                });
                // Kept on in prod on purpose (param values needed to debug user issues); JSON-blob
                // noise is suppressed by NLog (drop messages >5000 chars), not here.
                options.EnableSensitiveDataLogging(true);
                options.AddInterceptors(new QueryCountingInterceptor());
            });

            services.AddSingleton<DatabaseCache>();
            services.AddHostedService<UserCacheRefreshService>();
            services.AddHostedService<ActiveCoopsCacheRefreshService>();
            services.AddSingleton<CoopStatsCache>();
            services.AddSingleton<CoopAssignmentLookup>();
            services.AddHostedService<CoopAssignmentLookupRefreshService>();
            services.AddSingleton<Words>();
            services.AddSingleton<BotLogger>();

            var release = BuildConfig.IsRelease;
            if(!release) {
                services.AddSingleton<Bugsnag.IClient>(new Bugsnag.Client(new Bugsnag.Configuration("0")));
                var serviceCustomize = Type.GetType("EGG9000.Bot.ServiceCustomize");
                if(serviceCustomize is not null) {
                    var method = serviceCustomize.GetMethod("ConfigureServices");
                    method.Invoke(null, [hostContext, services]);
                    return;
                }
            }
            if(release) {
                logger.Log(NLog.LogLevel.Info, "RUNNING IN RELEASE");

                var bugsnagKey = SecretsHelper.GetConfigOrSecret(
                    hostContext.Configuration,
                    "ConnectionStrings:BugSnagApiKey",
                    "bugsnag_api_key");

                var bugsnagConfig = new Bugsnag.Configuration(bugsnagKey);

                var bs = new Bugsnag.Client(bugsnagConfig);

                services.AddSingleton<Bugsnag.IClient>(bs);

                services.AddBugsnag(options => {
                    options.ApiKey = bugsnagKey;
                    options.AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
                    options.ReleaseStage = "production";
                });
                
                var salt = SecretsHelper.GetConfigOrSecret(hostContext.Configuration, "ConnectionStrings:ApiSalt", "egg_inc_api_salt");
                if(String.IsNullOrEmpty(salt)) {
                    bs.Notify(new Exception("ApiSalt not found in secrets or configuration"));
                }
                logger.Log(NLog.LogLevel.Info, String.IsNullOrEmpty(salt) ? "ApiSalt not found" : "ApiSalt found");
                
                //var rabbitmqConn = SecretsHelper.GetConfigOrSecret(
                //    hostContext.Configuration,
                //    "ConnectionStrings:RabbitMQServer",
                //    "rabbitmq_connection");

                //services.AddOptions<RabbitMqTransportOptions>().Configure(options => {
                //    var host = rabbitmqConn?.Split("|");
                //    if(host?.Length > 1) {
                //        options.Host = host[0];
                //        options.User = host[1];
                //        options.Pass = host[2];
                //    }
                //});

                services.AddMassTransit(x => {
                    x.AddConsumer<ShutdownConsumer>();
                    x.AddConsumer<ExpireCacheConsumer>();
                    x.AddConsumer<RestartConsumer>();
                    // Per-instance temporary queue so a version update fans out to every running process
                    // instead of being load-balanced across a shared queue.
                    x.AddConsumer<UpdateApiVersionsConsumer>().Endpoint(e => { e.InstanceId = Guid.NewGuid().ToString("N"); e.Temporary = true; });
                    //if(string.IsNullOrEmpty(rabbitmqConn)) {
                        logger.Log(NLog.LogLevel.Info, "Using RabbitMQ In Memory");
                        x.UsingInMemory((context, cfg) => {
                            cfg.ConfigureEndpoints(context);
                            cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                        });
                    //} else {
                    //    logger.Log(NLog.LogLevel.Info, "Using RabbitMQ Server");
                    //    x.UsingRabbitMq((context, cfg) => {
                    //        cfg.ConfigureEndpoints(context);
                    //    });
                    //}
                });
            } else {
                logger.Log(NLog.LogLevel.Info, "RUNNING IN DEBUG");
                services.AddBugsnag();
                // DiscordQueueService (and other singleton background services) require a singleton
                // Bugsnag.IClient, which is only registered in the RELEASE branch. Register a suppressed
                // client in Debug so local CLI runs resolve. ReleaseStage is excluded from NotifyReleaseStages
                // so nothing is actually reported.
                services.AddSingleton<Bugsnag.IClient>(new Bugsnag.Client(new Bugsnag.Configuration("00000000000000000000000000000000") {
                    ReleaseStage = "development",
                    NotifyReleaseStages = ["production"],
                }));
                services.AddSingleton<IPublishEndpoint>(new PublishEndpointMock());
            }

            services.AddSingleton<DiscordHostedService>();
            services.AddSingleton(provider => provider.GetRequiredService<DiscordHostedService>().Gateway);
            services.AddSingleton(provider => new InteractionService(
                provider.GetRequiredService<DiscordHostedService>().Gateway,
                new InteractionServiceConfig {
                    DefaultRunMode = RunMode.Sync,
                    UseCompiledLambda = true
                }));
            services.AddHostedService<InteractionRoutingService>();

            var allowlist = ServiceAllowlist.Default;
            var skipped = new List<string>();
            void AddGated<T>(Func<IServiceProvider, T> factory = null) where T : class, IHostedService {
                if(!allowlist.IsEnabled(typeof(T))) {
                    skipped.Add(typeof(T).Name);
                    return;
                }
                if(factory is null) {
                    services.AddHostedService<T>();
                } else {
                    services.AddHostedService(factory);
                }
            }

            services.Configure<UpdaterOptions<LeaderboardUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(15));
            AddGated<LeaderboardUpdater>();

            AddGated<ArtifactCheaters>();
            AddGated<StaffCoopsMessage>();
            AddGated<CoopStatsRefreshService>();
            AddGated<EventUpdater>();

            services.Configure<UpdaterOptions<ThreadsCoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(5));
            services.AddSingleton<ThreadsCoopStatusUpdater>();
            AddGated(provider => provider.GetService<ThreadsCoopStatusUpdater>());

            services.AddSingleton<ContractUpdater>();
            AddGated(provider => provider.GetService<ContractUpdater>());

            AddGated<UserCXPUpdater>();
            AddGated<NewContracts>();
            AddGated<CreateCoopViaAPI>();
            AddGated<CreateCoopThreads>();
            AddGated<ShipReturnDM>();
            AddGated<UserSnapShots>();
            AddGated<ManageOverflow>();
            AddGated<RemoveTempRoles>();
            AddGated<HandleGradeChanges>();
            AddGated<RefreshNasaApod>();
            AddGated<UpdateBackups>();
            AddGated<CleanAutomationLogs>();
            AddGated<CleanApiKeyRequestLogs>();
            AddGated<RankupMessageSeeder>();
            AddGated<StorageSweep>();

            services.AddSingleton<CoopsBeingCreatedService>();
            services.AddSingleton<JobService>();
            services.AddHostedService(provider => provider.GetService<JobService>());

            AddGated<MessageHandlerService>();
            AddGated<DiscordUserService>();
            AddGated<UserGrades>();

            // Publishes a runtime snapshot over the bus every 15s; the site re-exposes it as bot_*
            // gauges on its /metrics for cross-scope reporting.
            services.AddHostedService<BotMetricsPublisher>();

            if(allowlist.Active) {
                logger.Info("Service allowlist active ({entries}); skipped hosted services: {skipped}",
                    string.Join(", ", allowlist.Entries), string.Join(", ", skipped));
            }
        } catch(Exception e) {
            logger.Error(e, "Stopped program because of exception");
            throw;
        } finally {
            NLog.LogManager.Shutdown();
        }
    }
}
