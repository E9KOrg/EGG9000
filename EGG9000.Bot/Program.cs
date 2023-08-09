using Discord.WebSocket;
using EGG9000.Bot;
using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EGG9000.Common.Services;
using Bugsnag.AspNet.Core;
using EGG9000.Bot.Services;
using NLog;
using NLog.Web;
using Microsoft.Extensions.Logging;
using EGG9000.Common.Factories;
using EGG9000.Common.Mocks;
using MassTransit;
using EGG9000.Bot.Consumers;

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
            method.Invoke(null, new object[] { hostContext, services });
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
            services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

            services.Configure<UpdaterOptions<LeaderboardUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(15));
            services.AddHostedService<LeaderboardUpdater>();

            services.AddHostedService<ArtifactCheaters>();

            services.AddHostedService<StaffCoopsMessage>();
            services.AddHostedService<EventUpdater>();
            services.AddHostedService<CoopReorder>();
            services.AddHostedService<CoopDeleteChannel>();

            services.Configure<UpdaterOptions<CoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(5));
            services.AddSingleton<CoopStatusUpdater>();
            services.AddHostedService<CoopStatusUpdater>(provider => provider.GetService<CoopStatusUpdater>());

            services.AddSingleton<ContractUpdater>();
            services.AddHostedService<ContractUpdater>(provider => provider.GetService<ContractUpdater>());

            services.AddHostedService<UserCxpUpdater>();
            services.AddHostedService<NewContracts>();
            services.AddHostedService<CreateCoopChannels>();
            services.AddHostedService<ShipReturnDM>();
            services.AddHostedService<UserSnapShots>();
            services.AddHostedService<ManageOverflow>();
            services.AddHostedService<RemoveTempRoles>();
            services.AddHostedService<HandleGradeChanges>();

            services.AddHostedService<JobService>();

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