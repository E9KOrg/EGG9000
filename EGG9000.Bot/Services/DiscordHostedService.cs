using Discord;
using Discord.WebSocket;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class DiscordHostedService : DiscordSocketClient {
        private IConfiguration _configuration;
        private static DiscordSocketConfig config = new DiscordSocketConfig() {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessages
        };
        public DiscordHostedService(IConfiguration Configuration) : base(config) {
            _configuration = Configuration;
            Console.WriteLine("Downloading all guild user");
            foreach(var guild in this.Guilds) {
                guild.DownloadUsersAsync().Wait();
            }

            this.Log += PrintLog;


            this.LoginAsync(TokenType.Bot, _configuration["ConnectionStrings:Token"]).Wait();
            this.StartAsync().Wait();

            Console.WriteLine("Waiting on Discord Connect");

            while(this.ConnectionState != ConnectionState.Connected) { }

            this.SetGameAsync("").Wait();
        }


        private Task PrintLog(LogMessage msg) {
            if(!msg.ToString().Contains("Rate limit triggered")) {
                Console.WriteLine(msg.ToString());
            }
            return Task.CompletedTask;
        }
    }
}
