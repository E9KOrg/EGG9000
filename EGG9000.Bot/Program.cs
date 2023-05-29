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
using Google.Protobuf;
using EGG9000.Common.Contracts;
using NLog;
using NLog.Web;
using Microsoft.Extensions.Logging;
using EGG9000.Common.Factories;

await Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging => {
        logging.ClearProviders();
        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
    }).UseNLog()
//.UseWindowsService()
    .ConfigureAppConfiguration((context, config) => {
        config.AddUserSecrets<Program>();
    })
    .UseDefaultServiceProvider(options => options.ValidateScopes = false)
    .ConfigureServices((hostContext, services) => {

        var logger = LogManager.Setup()
                                   .GetCurrentClassLogger();
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
#if DEBUG || DEV9002
            services.AddBugsnag();
            services.AddSingleton<DiscordHostedService>();
            services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());
            services.AddSingleton<APILink>();
            services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

            services.AddHostedService<CommandService>();
            //services.AddHostedService<DiscordUserService>();
            //services.AddHostedService<StaffCoopsMessage>();
            //services.AddHostedService<EventUpdater>();
            //services.AddHostedService<CoopReorder>();
            //services.AddHostedService<CoopDeleteChannel>();


            services.Configure<UpdaterOptions<CoopStatusUpdater>>(x => x.DelayStart = TimeSpan.FromHours(1));
            services.AddSingleton<CoopStatusUpdater>();
            services.AddHostedService<CoopStatusUpdater>(provider => provider.GetService<CoopStatusUpdater>());

            services.Configure<UpdaterOptions<ContractUpdater>>(x => x.DelayStart = TimeSpan.FromHours(1));
            services.AddSingleton<ContractUpdater>();
            services.AddHostedService<ContractUpdater>(provider => provider.GetService<ContractUpdater>());

            //services.Configure<UpdaterOptions<UserCxpUpdater>>(x => x.DelayStart = TimeSpan.Zero);
            //services.AddHostedService<UserCxpUpdater>();

            //services.AddHostedService<NewContracts>();
            //services.AddHostedService<CreateCoopChannels>();
            //services.AddHostedService<ShipReturnDM>();
            //services.AddHostedService<UserSnapShots>();
            //services.Configure<UpdaterOptions<LeaderboardUpdater>>(x => x.DelayStart = TimeSpan.FromHours(0));

            //services.Configure<UpdaterOptions<LeaderboardUpdater>>(x => x.DelayStart = TimeSpan.Zero);
            //services.AddHostedService<LeaderboardUpdater>();
            services.AddHostedService<ManageOverflow>();
            //services.AddHostedService<RemoveTempRoles>();
            //services.AddHostedService<HandleGradeChanges>();

            //services.AddHostedService<TestService>();
            //services.AddHostedService<TestUpdater>();

            //services.AddHostedService<UpcomingContracts>();

            logger.Log(NLog.LogLevel.Info, "RUNNING IN DEBUG");


#else
            logger.Log(NLog.LogLevel.Info, "RUNNING IN RELEASE");
        services.AddBugsnag(configuration => {
            configuration.ApiKey = hostContext.Configuration.GetConnectionString("BugSnagApiKey");
        });


        services.AddSingleton<DiscordHostedService>();
        services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());
        services.AddSingleton<APILink>();
        services.AddHostedService<APILink>(provider => provider.GetService<APILink>());

        services.AddHostedService<LeaderboardUpdater>();

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
        services.Configure<UpdaterOptions<LeaderboardUpdater>>(x => x.DelayStart = TimeSpan.FromMinutes(15));
        services.AddHostedService<ManageOverflow>();
        services.AddHostedService<RemoveTempRoles>();
        services.AddHostedService<HandleGradeChanges>();

        services.AddHostedService<CommandService>();
        services.AddHostedService<DiscordUserService>();
#endif
        } catch(Exception e) {
            logger.Error(e, "Stopped program because of exception");
            throw;
        } finally {
            LogManager.Shutdown();
        }

    }).Build().RunAsync();

public class TestService : IHostedService {
    public DiscordHostedService _client { get; }
    public ApplicationDbContext _db { get; }
    public Words _words { get; }
    private APILink _apilink { get; set; }
    private ILogger<TestService> _logger;
    private IHostApplicationLifetime _applicationLifetime;
    private IConfiguration _configuration;

    public TestService(DiscordHostedService client, ApplicationDbContext applicationDbContext, Words words, APILink apilink, ILogger<TestService> logger, IHostApplicationLifetime applicationLifetime, IConfiguration configuration) {
        _client = client;
        _db = applicationDbContext;
        _words = words;
        _apilink = apilink;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
        _configuration = configuration;
    }
    //public TestService(ApplicationDbContext applicationDbContext) {
    //    db = applicationDbContext;
    //}


    public async Task StartAsync(CancellationToken cancellationToken) {
        var config = _configuration.GetValue("ConfigSource", typeof(string));

        var user = await _db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == 248865520756064257);



        _ = 1;
        //_applicationLifetime.StopApplication();
    }
    public T ParseMessage<T>(string message) where T : Google.Protobuf.IMessage<T>, new() {
        var parse1 = new MessageParser<T>(() => new T());
        return parse1.ParseFrom(Convert.FromBase64String(message));
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

    public async Task FixDuplicateCoops() {
        var coops = await _db.Coops.Where(x => x.Created > new DateTimeOffset(2023, 5, 1, 0, 0, 0, TimeSpan.FromHours(-5))).ToListAsync();

        var dups = coops.GroupBy(x => new { Name = x.Name.ToLower(), x.ContractID }).Where(x => x.Count() > 1);


        foreach(var dup in dups) {
            foreach(var extra in dup.OrderBy(x => x.Created).Skip(1)) {

                if(!extra.DeletedChannel) {
                    var channel = _client.GetGuild(extra.DiscordChannelId);
                    if(channel is not null) {
                        await channel.DeleteAsync();
                        _logger.LogInformation("Deleting channel for {coop}", extra.Name);
                    }
                }
                var xrefs = await _db.UserCoopXrefs.Where(x => x.CoopId == extra.Id).ToListAsync();
                _db.RemoveRange(xrefs);
                _db.Remove(extra);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Deleting {coop} from database", extra.Name);
            }
        }
    }
}