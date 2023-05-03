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
using EGG9000.Common.Services;
using Bugsnag.AspNet.Core;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database.Entities;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using EGG9000.Bot.Services;
using EGG9000.Common.Migrations;
using UserSnapShots = EGG9000.Bot.Automated.UserSnapShots;
using Ei;
using Google.Protobuf;

await Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices((hostContext, services) => {
        Console.WriteLine("Main Start");

        var Configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        services.AddSingleton<IConfiguration>(Configuration);
        services.Configure<HostOptions>(options => {
            options.ShutdownTimeout = TimeSpan.FromMinutes(5);
        });

        services.AddDbContext<ApplicationDbContext>(//options =>
            //options.UseSqlServer(
                //Configuration.GetConnectionString("DefaultConnection"))
            );

        services.AddSingleton<Words>();
        services.AddMemoryCache();
#if DEBUG
        services.AddBugsnag();
        services.AddSingleton<DiscordHostedService>();
        services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());
        services.AddSingleton<APILink>();
        services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

        //services.AddHostedService<CommandService>();
        //services.AddHostedService<DiscordUserService>();
        //services.AddHostedService<StaffCoopsMessage>();
        //services.AddHostedService<EventUpdater>();
        //services.AddHostedService<CoopReorder>();
        //services.AddHostedService<CoopDeleteChannel>();

        //services.Configure<UpdaterOptions<CoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromHours(1));
        //services.AddSingleton<CoopStatusUpdater>();
        //services.AddHostedService<CoopStatusUpdater>(provider => provider.GetService<CoopStatusUpdater>());

        //services.Configure<UpdaterOptions<ContractUpdater>>(x => x.DelayStart = TimeSpan.FromHours(1));
        services.AddSingleton<ContractUpdater>();
        services.AddHostedService<ContractUpdater>(provider => provider.GetService<ContractUpdater>());

        //services.AddHostedService<NewContracts>();
        //services.AddHostedService<CreateCoopChannels>();
        //services.AddHostedService<ShipReturnDM>();
        //services.AddHostedService<UserSnapShots>();
        //services.AddHostedService<LeaderboardUpdater>();
        //services.AddHostedService<ManageOverflow>();
        //services.AddHostedService<RemoveTempRoles>();

        //services.AddHostedService<TestService>();
        //services.AddHostedService<TestUpdater>();

        //services.AddHostedService<UpcomingContracts>();

        Console.WriteLine("RUNNING IN DEBUG");


#else
        Console.WriteLine("RUNNING IN RELEASE");
        services.AddBugsnag(configuration => {
            configuration.ApiKey = Configuration.GetConnectionString("BugSnagApiKey");
        });

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

        services.Configure<UpdaterOptions<CoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(5));
        services.AddSingleton<CoopStatusUpdater>();
        services.AddHostedService<CoopStatusUpdater>(provider => provider.GetService<CoopStatusUpdater>());

        services.AddSingleton<ContractUpdater>();
        services.AddHostedService<ContractUpdater>(provider => provider.GetService<ContractUpdater>());

        services.AddHostedService<NewContracts>();
        services.AddHostedService<CreateCoopChannels>();
        services.AddHostedService<ShipReturnDM>();
        services.AddHostedService<UserSnapShots>();
        services.Configure<UpdaterOptions<LeaderboardUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(15));
        services.AddHostedService<LeaderboardUpdater>();
        services.AddHostedService<ManageOverflow>();
        services.AddHostedService<RemoveTempRoles>();
#endif


    }).ConfigureAppConfiguration((context, config) => {
        config.AddUserSecrets<Secrets>();
    }).Build().RunAsync();

public class TestService : IHostedService {
    //public TestService(DiscordHostedService client, ApplicationDbContext applicationDbContext) {
    //    _client = client;
    //    db = applicationDbContext;
    //}
    public TestService(ApplicationDbContext applicationDbContext) {
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


        //var guildContract = await db.GuildContracts.Include(x => x.Contract).FirstAsync(x => x.ContractID == "arteggmis-2022" && x.GuildID == 656455567858073601 && x.League == 0);
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
        //var coopStatus = await ContractsAPI.GetCoopStatus("toy-builders-2020", "joygains41".ToLower().Trim());
        //var r = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
        //    ClientVersion = ContractsAPI.ClientVersion,
        //    ContractIdentifier = coopStatus.ContractIdentifier,
        //    CoopIdentifier = coopStatus.CoopIdentifier,
        //    PlayerIdentifier = "EI4599706528776192",
        //    Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
        //    RequestingUserId = coopStatus.CreatorId
        //}, ContractsAPI.UserId);
        //var raw = await ContractsAPI.FirstContactRaw("EI5015560351383552");
        //var user = db.DBUsers.First(x => x.DiscordId == 710137969465491498);
        //var backup = await ContractsAPI.FirstContact(user.Backups.First().EggIncId);
        //var response = await ContractsAPI.Post<PeriodicalsResponse, GetPeriodicalsRequest>(new GetPeriodicalsRequest { 
        //    UserId = "EI6229292070993920",
        //     ArtifactsUnlocked = true, ContractsUnlocked = true, CurrentClientVersion = ContractsAPI.ClientVersion, SoulEggs = user.Backups.First().SoulEggs, SecondsFullRealtime = 100000, SecondsFullGametime = 100000, PiggyFoundFull = true, PiggyFull = true, Eop =1000,MysticalEarningsMult = 0, LostIncrements = 0
        //}, "EI6229292070993920", true);
        //var t = response.ContractPlayerInfo;

        //var response = await ContractsAPI.GetCoopStatus("a-new-grade", "test3");
        //var backup = await ContractsAPI.FirstContact("EI5932295321550848");
        //var customBackup = new CustomBackup(backup.Backup);
        //var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == 170412210076516352);
        //var req = "CgthLW5ldy1ncmFkZRIGdGVzdDM0GhJFSTUyMjMyOTk1MTgzMDAxNjAgLio1ChJFSTUyMjMyOTk1MTgzMDAxNjAQLhoEMS4yNiIIMS4yNi4wLjUqA0lPUzICVVM6AmVuQAA=";
        //var reqarray = System.Convert.FromBase64String(req);
        //var parse = new MessageParser<ContractCoopStatusRequest>(() => new ContractCoopStatusRequest());
        //var p = parse.ParseFrom(reqarray);

        var authString = "CucBCgthLW5ldy1ncmFkZRIFdGVzdDMaeGdBQUFBQUJrVVlKRGs4aFU5RFpRRVIzSFpHLTB0Q3ByQzE1aXMwNVpodElTSjNfbXl3V2VFYU9ka20wdzZaektnaWJHMndXRDJvYlNGWXVsdFo5WHJBSU9wekMtLXhsWElmZlBMTWZOTlo5dElBc0NGT3g2a1ZNPSISRUk1MjIzMjk5NTE4MzAwMTYwKAEyCEtlbmRyb21lOC5CNQoSRUk1MjIzMjk5NTE4MzAwMTYwEC4aBDEuMjYiCDEuMjYuMC41KgNJT1MyAlVTOgJlbkAAEkBmYTQyNGFjYzY4OTY0ZjA1ZTQ1ZDBhNDRmN2RhYjkyMzBlM2I2MjdkODZjZGY4MzkyYjVjZDIxYTA1NzE5MjMz";
        var parse1 = new MessageParser<AuthenticatedMessage>(() => new AuthenticatedMessage());
        var res = parse1.ParseFrom(System.Convert.FromBase64String(authString));
        var parse2 = new MessageParser<GiftPlayerCoopRequest>(() => new GiftPlayerCoopRequest());
        var res2 = parse2.ParseFrom(res.Message);

    }

    public DateTime RoundToNearest(DateTime dt, TimeSpan d) {
        var delta = dt.Ticks % d.Ticks;
        bool roundUp = delta > d.Ticks / 2;
        var offset = roundUp ? d.Ticks : 0;

        return new DateTime(dt.Ticks + offset - delta, dt.Kind);
    }
    public List<List<T>> ChunkBy<T>(List<T> source, int chunkSize) {
        return source
            .Select((x, i) => new { Index = i, Value = x })
            .GroupBy(x => x.Index / chunkSize)
            .Select(x => x.Select(v => v.Value).ToList())
            .ToList();
    }

    public Task StopAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}