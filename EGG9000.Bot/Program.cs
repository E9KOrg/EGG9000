using Bugsnag.AspNet.Core;
using Discord.WebSocket;
using EGG9000.Bot;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.Consumers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Factories;
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

await Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging => {
        logging.ClearProviders();
        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
    })
    .UseNLog()
    .ConfigureAppConfiguration((context, config) => {
        config.AddUserSecrets<Program>();
    })
    .UseDefaultServiceProvider(options => options.ValidateScopes = false)
    .ConfigureServices(ConfigureServices)
    .Build()
    .RunAsync();

void ConfigureServices(HostBuilderContext hostContext, IServiceCollection services) {
    var logger = LogManager.Setup().GetCurrentClassLogger();
    logger.Log(NLog.LogLevel.Info, "Main Start");
    try {
        var serviceProvider = services.BuildServiceProvider();
        StaticLoggerFactory.Initialize(serviceProvider.GetRequiredService<ILoggerFactory>());

        services.Configure<HostOptions>(options => {
            options.ShutdownTimeout = TimeSpan.FromMinutes(5);
        });

        services.AddDbContext<ApplicationDbContext>(options => {
            options.UseSqlServer(hostContext.Configuration.GetConnectionString("DefaultConnection"), x => x.MigrationsAssembly("EGG9000.Common"));
            options.EnableSensitiveDataLogging(true);
        });

        services.AddDbContextFactory<ApplicationDbContext>(options => {
            options.UseSqlServer(hostContext.Configuration.GetConnectionString("DefaultConnection"), x => x.MigrationsAssembly("EGG9000.Common"));
            options.EnableSensitiveDataLogging(true);
        });

        services.AddSingleton<Words>();
        services.AddMemoryCache();

        services.Configure<APILinkOptions>(x => x.ReportUpdatedClientVersion = true);

#if DEBUG
            var debug = true;
#else
        var debug = false;
#endif
        //This will allow you to configure which parts of the bot you want to run if you don't want everything to run
        var serviceCustomize = Type.GetType("EGG9000.Bot.ServiceCustomize");
        if(serviceCustomize is not null && debug) {
            var method = serviceCustomize.GetMethod("ConfigureServices");
            method.Invoke(null, [hostContext, services]);
        } else {

#if RELEASE
        logger.Log(NLog.LogLevel.Info, "RUNNING IN RELEASE");
        services.AddBugsnag(configuration => {
            configuration.ApiKey = hostContext.Configuration.GetConnectionString("BugSnagApiKey");
        });
        services.AddMassTransit(x => {
            x.AddConsumer<ShutdownConsumer>();
            x.UsingRabbitMq((context, cfg) => {
                cfg.ConfigureEndpoints(context);
            });
        });

#else
            logger.Log(NLog.LogLevel.Info, "RUNNING IN DEBUG");
            services.AddBugsnag();
            services.AddSingleton<IPublishEndpoint>(new PublishEndpointMock());
#endif

            services.AddSingleton<DiscordHostedService>();
            services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());
            services.AddSingleton<APILink>();
            services.AddHostedService(provider => provider.GetService<APILink>());

            services.Configure<UpdaterOptions<LeaderboardUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(15));
            services.AddHostedService<LeaderboardUpdater>();

            services.AddHostedService<ArtifactCheaters>();

            services.AddHostedService<StaffCoopsMessage>();
            services.AddHostedService<EventUpdater>();
            services.AddHostedService<CoopReorder>();
            services.AddHostedService<CoopDeleteChannel>();

            services.Configure<UpdaterOptions<CoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(5));
            services.AddSingleton<CoopStatusUpdater>();
            services.AddHostedService(provider => provider.GetService<CoopStatusUpdater>());

            services.Configure<UpdaterOptions<ThreadsCoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(5));
            services.AddSingleton<ThreadsCoopStatusUpdater>();
            services.AddHostedService(provider => provider.GetService<ThreadsCoopStatusUpdater>());

            services.AddSingleton<ContractUpdater>();
            services.AddHostedService(provider => provider.GetService<ContractUpdater>());

            services.AddHostedService<UserCxpUpdater>();
            services.AddHostedService<NewContracts>();
            services.AddHostedService<CreateCoopThreads>();
            //services.AddHostedService<CreateCoopChannels>();
            services.AddHostedService<ShipReturnDM>();
            services.AddHostedService<UserSnapShots>();
            services.AddHostedService<ManageOverflow>();
            services.AddHostedService<RemoveTempRoles>();
            services.AddHostedService<HandleGradeChanges>();


            services.AddSingleton<JobService>();
            services.AddHostedService(provider => provider.GetService<JobService>());

            services.AddHostedService<CommandService>();
            services.AddHostedService<DiscordUserService>();
        }
    } catch(Exception e) {
        logger.Error(e, "Stopped program because of exception");
        throw;
    } finally {
        LogManager.Shutdown();
    }
}