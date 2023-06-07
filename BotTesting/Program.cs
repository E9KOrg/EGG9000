// See https://aka.ms/new-console-template for more information
using Bugsnag.AspNet.Core;

using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NLog;
using NLog.Web;

using TestBot;

await Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging => {
        logging.ClearProviders();
        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
    }).UseNLog()
    .ConfigureServices((hostContext, services) => {
        var logger = LogManager.Setup()
                           .GetCurrentClassLogger();
        logger.Log(NLog.LogLevel.Info, "Main Start");


        var Configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        services.Configure<HostOptions>(options => {
            options.ShutdownTimeout = TimeSpan.FromMinutes(5);
        });

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                Configuration.GetConnectionString("DefaultConnection")));

        services.AddMassTransit(x => {
            x.AddConsumer<ShutdownConsumer>();
            x.UsingRabbitMq((context, cfg) => {
                cfg.ConfigureEndpoints(context);
            });
        });
        services.AddBugsnag();
        services.AddMemoryCache();
        services.AddSingleton<DiscordHostedService>();
        services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());
        //services.AddSingleton<APILink>();
        //services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

        services.AddHostedService<CommandService>();
        //services.AddHostedService<UpcomingContracts>();


    }).ConfigureAppConfiguration((context, config) => {
        config.AddUserSecrets<Program>();
    }).Build().RunAsync();