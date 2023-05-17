// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NLog;
using NLog.Extensions.Logging;
using NLog.Web;

using System.Text;

await Host.CreateDefaultBuilder(args)
    
    .ConfigureLogging(logging => {
        logging.ClearProviders();
        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
        //logging.AddNLog();
    })
    .UseNLog()
    //.UseWindowsService()
    .ConfigureServices((hostContext, services) => {
        var Configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        var logger = LogManager.Setup()
                                   .SetupExtensions(ext => ext.RegisterConfigSettings(Configuration))
                                   .GetCurrentClassLogger();
        logger.Log(NLog.LogLevel.Info, "Main Start");
        try {


            //services.AddLogging(builder => {
            //    //builder.ClearProviders();
            //    builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            //    builder.AddNLog(Configuration);
            //});

            services.AddHostedService<TestService>();
        } catch(Exception e) {
            logger.Error(e, "Stopped program because of exception");
            throw;
        } finally {
            LogManager.Shutdown();
        }

    }).ConfigureAppConfiguration((context, config) => {
    }).Build().RunAsync();




//var config = new ConfigurationBuilder().Build();

//var logger = LogManager.Setup()
//                       .SetupExtensions(ext => ext.RegisterConfigSettings(config))
//.GetCurrentClassLogger();

//try {
//    using var servicesProvider = new ServiceCollection()
//        .AddHostedService<TestService>()
//        .AddTransient<Runner>() // Runner is the custom class
//        .AddLogging(loggingBuilder => {
//            // configure Logging with NLog
//            //loggingBuilder.ClearProviders();
//            loggingBuilder.AddConsole();
//            loggingBuilder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
//            loggingBuilder.AddNLog(config);
//        }).BuildServiceProvider();

//    var runner = servicesProvider.GetRequiredService<Runner>();
//    runner.DoAction("Action1");

//    Console.WriteLine("Press ANY key to exit");
//    Console.ReadKey();
//} catch(Exception ex) {
//    // NLog: catch any exception and log it.
//    logger.Error(ex, "Stopped program because of exception");
//    throw;
//} finally {
//    // Ensure to flush and stop internal timers/threads before application-exit (Avoid segmentation fault on Linux)
//    LogManager.Shutdown();
//}


//public class Runner {
//    private readonly ILogger<Runner> _logger;

//    public Runner(ILogger<Runner> logger) {
//        _logger = logger;
//    }

//    public void DoAction(string name) {
//        _logger.LogDebug(20, "Doing hard work! {Action}", name);
//        _logger.LogInformation(21, "Doing hard work! {Action}", name);
//        _logger.LogWarning(22, "Doing hard work! {Action}", name);
//        _logger.LogError(23, "Doing hard work! {Action}", name);
//        _logger.LogCritical(24, "Doing hard work! {Action}", name);
//    }
//}



public class TestService : IHostedService {
    private ILogger<TestService> _logger;

    public TestService(ILogger<TestService> logger) {
        _logger = logger;
    }


    public async Task StartAsync(CancellationToken cancellationToken) {
        var variable = new { val = 1234 };
        _logger.Log(Microsoft.Extensions.Logging.LogLevel.Information, "Test log {Variable}", variable);
        _logger.LogError("Test");
    }
    public Task StopAsync(CancellationToken cancellationToken) {
        return Task.CompletedTask;
    }
}