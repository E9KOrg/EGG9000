// See https://aka.ms/new-console-template for more information
using Bugsnag.AspNet.Core;

using Discord.WebSocket;

//using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using NLog;
using NLog.Web;


//using TestBot;

//await Host.CreateDefaultBuilder(args)
//    .ConfigureLogging(logging => {
//        logging.ClearProviders();
//        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
//    }).UseNLog()
//    .ConfigureServices((hostContext, services) => {
//        var logger = LogManager.Setup()
//                           .GetCurrentClassLogger();
//        logger.Log(NLog.LogLevel.Info, "Main Start");


//        var Configuration = new ConfigurationBuilder()
//            .AddUserSecrets<Program>()
//            .Build();

//        services.Configure<HostOptions>(options => {
//            options.ShutdownTimeout = TimeSpan.FromMinutes(5);
//        });

//        services.AddDbContext<ApplicationDbContext>(options =>
//            options.UseSqlServer(
//                Configuration.GetConnectionString("DefaultConnection")));

//        //services.AddMassTransit(x => {
//        //    x.AddConsumer<ShutdownConsumer>();
//        //    x.UsingRabbitMq((context, cfg) => {
//        //        cfg.ConfigureEndpoints(context);
//        //    });
//        //});
//        services.AddBugsnag();
//        services.AddMemoryCache();
//        services.AddSingleton<DiscordHostedService>();
//        services.AddSingleton<DiscordSocketClient>(provider => provider.GetService<DiscordHostedService>());

//        //services.AddHostedService<CommandService>();
//        //services.AddHostedService<UpcomingContracts>();


//    }).ConfigureAppConfiguration((context, config) => {
//        config.AddUserSecrets<Program>();
//    }).Build().RunAsync();


var bugsnag = new Bugsnag.Client(new Bugsnag.Configuration("d5141d01f0e9f20506c3dcaa9110fe01"));
//Bugsnag.AspNet.Core.HttpContextExtensions.ToRequest
//new Bugsnag.Configuration { }

//try {
//    throw new System.NotImplementedException();
//} catch(System.Exception ex) {
//    bugsnag.Notify(ex);
//}

//await Task.Delay(1000);
Console.WriteLine(JsonConvert.SerializeObject(bugsnag.Configuration));