using Discord.WebSocket;
using EGG9000.Bot;
using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EGG9000.Bot.Services;
using Bugsnag.AspNet.Core;




await Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices((hostContext, services) => {
        Console.WriteLine("Main Start");

        //_bugsnag = new Bugsnag.Client(new Bugsnag.Configuration("c924bd8a1fd56db4552e0549a76d3689"));
        //services.AddSingleton<Bugsnag.IClient>(_bugsnag);
        services.AddBugsnag(configuration => {
            configuration.ApiKey = "c924bd8a1fd56db4552e0549a76d3689";
        });

        var Configuration = new ConfigurationBuilder()
            .AddUserSecrets<Secrets>()
            .Build();



        services.Configure<HostOptions>(options => {
            options.ShutdownTimeout = TimeSpan.FromMinutes(5);
        });

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                Configuration.GetConnectionString("DefaultConnection")));

        //services.AddHostedService<DatabaseQueue>();
        services.AddSingleton<Words>();


        services.AddSingleton<DiscordHostedService>();
        services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());
        services.AddSingleton<APILink>();
        services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

        services.AddHostedService<SlashCommandService>();
        services.AddHostedService<TextCommandService>();
        services.AddHostedService<DiscordUserService>();
        services.AddHostedService<StaffCoopsMessage>();
        services.AddHostedService<EventUpdater>();
        services.AddHostedService<CoopReorder>();
        services.AddHostedService<CoopDeleteChannel>();

        services.AddSingleton<CoopStatusUpdater>();
        services.AddHostedService<CoopStatusUpdater>(provider => provider.GetService<CoopStatusUpdater>());

        services.AddSingleton<ContractUpdater>();
        services.AddHostedService<ContractUpdater>(provider => provider.GetService<ContractUpdater>());

        services.AddHostedService<NewContracts>();
        services.AddHostedService<CreateCoopChannels>();
        services.AddHostedService<ShipReturnDM>();
        services.AddHostedService<UserSnapShots>();
        services.AddHostedService<LeaderboardUpdater>();
        services.AddHostedService<ManageOverflow>();


    }).ConfigureAppConfiguration((context, config) => {
        // configure the app here.
        config.AddUserSecrets<Secrets>();
    }).Build().RunAsync();

