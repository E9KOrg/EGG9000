using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class DiscordBasicService : DiscordSocketClient {
        public bool IsReady { get; private set; }
        private IConfiguration _configuration;
        private ILogger<DiscordBasicService> _logger;
        private static DiscordSocketConfig config = new DiscordSocketConfig() {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.Guilds | GatewayIntents.GuildMessages | 
                             GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessages | GatewayIntents.MessageContent
        };
        public DiscordBasicService(IConfiguration Configuration, ILogger<DiscordBasicService> logger) : base(config) {
            _configuration = Configuration;
            _logger = logger;

            this.Log += PrintLog;
            this.Ready += DiscordHostedService_Ready;
            this.LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();
            this.StartAsync().Wait();

            _logger.Log(LogLevel.Information, "Waiting on Discord Connect");

            while(this.ConnectionState != ConnectionState.Connected) {
                
             }

            _logger.Log(LogLevel.Information, "Discord Ready");
        }

        private Task DiscordHostedService_Ready() {
            IsReady = true;
            this.SetGameAsync("").Wait();

            foreach(var guild in this.Guilds) {
                _logger.Log(LogLevel.Information, "Download guild users for {Guild}", guild.Name);

                guild.DownloadUsersAsync().Wait();
            }

            _logger.Log(LogLevel.Information, "Discord Ready");

            return Task.CompletedTask;
        }

        private Task PrintLog(LogMessage msg) {
            if(msg.ToString().Contains("Rate limit triggered")) {
                _logger.Log(LogLevel.Trace, "Discord Log: {msg}", msg.Message);
            } else if(msg.Exception is not null) {
                _logger.LogError(msg.Exception, "Discord Log: Exception Thrown");
            } else {
                _logger.Log(LogLevel.Information, "Discord Log: {msg}", msg.Message);
            }
            return Task.CompletedTask;
        }
    }
}
