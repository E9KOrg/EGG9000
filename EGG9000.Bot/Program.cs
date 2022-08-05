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
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database.Entities;
using System.Threading.Tasks;
using System.Threading;

await Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices((hostContext, services) => {
        Console.WriteLine("Main Start");

        var Configuration = new ConfigurationBuilder()
            .AddUserSecrets<Secrets>()
            .Build();

        services.Configure<HostOptions>(options => {
            options.ShutdownTimeout = TimeSpan.FromMinutes(5);
        });

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                Configuration.GetConnectionString("DefaultConnection")));

        services.AddSingleton<Words>();
        services.AddMemoryCache();
#if DEBUG
        services.AddBugsnag();
        services.AddSingleton<DiscordHostedService>();
        services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());
        services.AddSingleton<APILink>();
        services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

        //services.AddHostedService<CommandService>();
        //services.AddHostedService<TextCommandService>();
        //services.AddHostedService<DiscordUserService>();
        services.AddHostedService<StaffCoopsMessage>();
        //services.AddHostedService<EventUpdater>();
        //services.AddHostedService<CoopReorder>();
        //services.AddHostedService<CoopDeleteChannel>();

        //services.Configure<UpdaterOptions<CoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromHours(1));
        //services.AddSingleton<CoopStatusUpdater>();
        //services.AddHostedService<CoopStatusUpdater>(provider => provider.GetService<CoopStatusUpdater>());

        //services.Configure<UpdaterOptions<ContractUpdater>>(x => x.DelayStart = TimeSpan.FromHours(1));
        //services.AddSingleton<ContractUpdater>();
        //services.AddHostedService<ContractUpdater>(provider => provider.GetService<ContractUpdater>());

        //services.AddHostedService<NewContracts>();
        //services.AddHostedService<CreateCoopChannels>();
        //services.AddHostedService<ShipReturnDM>();
        //services.AddHostedService<UserSnapShots>();
        //services.AddHostedService<LeaderboardUpdater>();
        //services.AddHostedService<ManageOverflow>();
        //services.AddHostedService<RemoveTempRoles>();

        //services.AddHostedService<TestService>();
        //services.AddHostedService<TestUpdater>();

        //services.AddHostedService<ContextCommandService>();
        Console.WriteLine("RUNNING IN DEBUG");


#endif


#if !DEBUG
        Console.WriteLine("RUNNING IN RELEASE");
        services.AddBugsnag(configuration => {
            configuration.ApiKey = Configuration.GetConnectionString("BugSnagApiKey");
        });

        //services.AddHostedService<DatabaseQueue>();

        services.AddSingleton<DiscordHostedService>();
        services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());
        services.AddSingleton<APILink>();
        services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

        services.AddHostedService<CommandService>();
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
        services.AddHostedService<RemoveTempRoles>();
#endif


    }).ConfigureAppConfiguration((context, config) => {
        config.AddUserSecrets<Secrets>();
    }).Build().RunAsync();

public class TestService : IHostedService {
    public TestService(DiscordHostedService client) {
        Client = client;
    }

    public DiscordHostedService Client { get; }

    public async Task StartAsync(CancellationToken cancellationToken) {
        var f = await Client.GetChannelAsync(GuildChannelType.FaqChannel, Client.GetGuild(656455567858073601));
        var w = await Client.GetChannelAsync(GuildChannelType.Welcome, Client.GetGuild(656455567858073601));
        var g = await Client.GetChannelAsync(GuildChannelType.General, Client.GetGuild(656455567858073601));
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}