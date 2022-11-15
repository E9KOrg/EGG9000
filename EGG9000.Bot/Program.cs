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
using System.Linq;

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
        //services.AddHostedService<StaffCoopsMessage>();
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
        services.AddHostedService<LeaderboardUpdater>();
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
    public TestService(DiscordHostedService client, ApplicationDbContext applicationDbContext) {
        _client = client;
        db = applicationDbContext;
    }

    public DiscordHostedService _client { get; }
    public ApplicationDbContext db { get; }

    public async Task StartAsync(CancellationToken cancellationToken) {
        //var DMChannel = await Client.GetUser(620365345303167006).CreateDMChannelAsync();
        //await DMChannel.SendMessageAsync("Testing DM <@620365345303167006>");
        //Console.WriteLine("Sent DM");
        //var f = await Client.GetChannelAsync(GuildChannelType.FaqChannel, Client.GetGuild(656455567858073601));
        //var w = await Client.GetChannelAsync(GuildChannelType.Welcome, Client.GetGuild(656455567858073601));
        //var g = await Client.GetChannelAsync(GuildChannelType.General, Client.GetGuild(656455567858073601));
        //var status = await ContractsAPI.GetCoopStatus("all-or-nothing", "DailyFolic4".ToLower());
        //var r = await ContractsAPI.Send<Ei.KickPlayerCoopRequest>(new Ei.KickPlayerCoopRequest {
        //    ClientVersion = ContractsAPI.ClientVersion,
        //    ContractIdentifier = status.ContractIdentifier,
        //    CoopIdentifier = status.CoopIdentifier,
        //    PlayerIdentifier = status.Participants[0].UserId,
        //    Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
        //    RequestingUserId = status.CreatorId
        //}, status.CreatorId);


        //var guildContract = await db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == "arteggmis-2022" && x.GuildID == 656455567858073601 && x.Elite);
        //var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //await ContractUpdater.UpdateContractChannelName(guildContract, coopsBreakdown, (SocketTextChannel)_client.GetChannel(1011288737600258049));


        //var status = await ContractsAPI.GetCoopStatus("anniversary-edible", "BudGood16");
        //var user = status.Participants.First(x => x.UserName.Contains("Schrod"));
        //var eb = (Math.Pow(10, user.SoulPower) * 100).ToEggString();
        //Console.WriteLine(eb);
        //Console.WriteLine(user.RankChange);
        //var r2 = await ContractsAPI.GetCoopStatus("eggutate-2022", "pocket575");
        //var r = await ContractsAPI.Post<Ei.QueryCoopResponse, Ei.QueryCoopRequest>(new Ei.QueryCoopRequest {
        //    ClientVersion = ContractsAPI.ClientVersion, League = 0, ContractIdentifier = "eggutate-2022", CoopIdentifier = "pocket575", Rinfo = ContractsAPI.GetInfo(ContractsAPI.UserId)
        //}, ContractsAPI.UserId);
        //var coopStatus = await ContractsAPI.GetCoopStatus("quantum-voting-2020", "wecutter48".ToLower().Trim());
        //var r = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
        //    ClientVersion = ContractsAPI.ClientVersion,
        //    ContractIdentifier = coopStatus.ContractIdentifier,
        //    CoopIdentifier = coopStatus.CoopIdentifier,
        //    PlayerIdentifier = "EI4885395808714752",
        //    Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
        //    RequestingUserId = coopStatus.CreatorId
        //}, ContractsAPI.UserId);
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}