using Bugsnag.AspNet.Core;
using Discord.WebSocket;
using EGG9000.Bot;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.Services;
using EGG9000.Common.Consumers;
using EGG9000.Common.Database;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
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
using System.Threading;
using System.Threading.Tasks;
using Prometheus;
using System.Reflection;
using System.Collections.Generic;

using Newtonsoft.Json;

// Set up logger before anything else
var logger = LogManager.Setup().GetCurrentClassLogger();
logger.Log(NLog.LogLevel.Info, "Main Start");

string botColor;

try
{
    // Build a minimal configuration to check BOT_ACTIVE before building the full host
    var tempConfig = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .AddUserSecrets<Program>(optional: true) // Optional for Docker scenarios
        .Build();

    var botActive = tempConfig.GetValue("BOT_ACTIVE", true);
    botColor = tempConfig.GetValue<string>("BOT_COLOR") ?? "blue";

#if DEBUG 
    botColor = "debug";
#endif
    NLog.GlobalDiagnosticsContext.Set("CustomMachineName", $"{Environment.MachineName}_{botColor}");
    NLog.GlobalDiagnosticsContext.Set("CustomAppName", $"EGG9000.Bot");
    logger.Log(NLog.LogLevel.Info, "CustomMachineName = " + $"{NLog.GlobalDiagnosticsContext.Get("CustomMachineName")}");

    logger.Log(NLog.LogLevel.Info, "BOT_ACTIVE = " + botActive);
    logger.Log(NLog.LogLevel.Info, "BOT_COLOR = " + botColor);

    if (!botActive)
    {
        logger.Log(NLog.LogLevel.Info, "Bot set to not active. Exiting gracefully without starting services.");
        LogManager.Shutdown();
        return; // Exit cleanly without throwing exception
    }

    // If BOT_ACTIVE is true, proceed with normal host build
    await Host.CreateDefaultBuilder(args)
        .ConfigureLogging(logging => {
            logging.ClearProviders();
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
        })
        .UseNLog()
        .ConfigureAppConfiguration((context, config) => {
            config.AddUserSecrets<Program>(optional: true); // Optional for Docker scenarios
        })
        .UseDefaultServiceProvider(options => options.ValidateScopes = false)
        .ConfigureServices(EGG9000.Bot.BotHostFactory.ConfigureServices)
        .Build()
        .RunAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "Fatal error during startup");
    LogManager.Shutdown();
    throw;
}
