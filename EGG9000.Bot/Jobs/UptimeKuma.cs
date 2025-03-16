using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using System.Threading;
using Ei;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Net.Http;
using Polly;

namespace EGG9000.Bot.Jobs {

    public class UptimeKuma(ILogger<SubscriptionsCheckJob> logger) {
        private readonly ILogger<SubscriptionsCheckJob> _logger = logger;
        private readonly HttpClient httpClient = new HttpClient();


        [Job("0/30 * * * * *")]
        public async Task Send() {
            var policy = Policy
               .Handle<Exception>()
               .WaitAndRetry(
               [
                 TimeSpan.FromSeconds(1),
                            TimeSpan.FromSeconds(3),
                            TimeSpan.FromSeconds(7)
               ]);


            //https://uptime.dev.sglade.com/api/push/yIc6q6ocjd?status=up&msg=OK&ping=


            try {
                var response = await policy.Execute(async () => await httpClient.GetAsync("https://uptime.dev.sglade.com/api/push/yIc6q6ocjd?status=up&msg=OK")); ;
                if(!response.IsSuccessStatusCode) {
                    _logger.LogError("Failed to send uptime notification");
                }
            } catch(Exception e) {
                _logger.LogError(e, "Failed to send uptime notification");
            }
        }
    }
}
