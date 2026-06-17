using EGG9000.Bot.Services;

using Microsoft.Extensions.Logging;


using System;
using System.Threading.Tasks;
using System.Net.Http;
using Polly;



#if RELEASE
namespace EGG9000.Bot.Jobs {

    public class UptimeKuma(ILogger<UptimeKuma> logger) {
        private readonly ILogger<UptimeKuma> _logger = logger;
        private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };


        [Job("0/30 * * * * *")]
        public async Task Send() {
            var policy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                [
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(7)
                ]);


            //https://uptime.dev.sglade.com/api/push/yIc6q6ocjd?status=up&msg=OK&ping=


            try {
                var response = await policy.ExecuteAsync(() =>
                    httpClient.GetAsync("https://uptime.dev.sglade.com/api/push/yIc6q6ocjd?status=up&msg=OK"));

                if (!response.IsSuccessStatusCode) {
                    _logger.LogError("Failed to send uptime notification");
                }
            } catch (Exception e) {
                _logger.LogError(e, "Failed to send uptime notification");
            }
        }
    }
}
#endif