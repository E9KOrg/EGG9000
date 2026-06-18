using EGG9000.Bot.Services;

using Microsoft.Extensions.Logging;


using System;
using System.Threading.Tasks;
using System.Net.Http;



#if RELEASE
namespace EGG9000.Bot.Jobs {

    public class UptimeKuma(ILogger<UptimeKuma> logger) {
        private readonly ILogger<UptimeKuma> _logger = logger;
        private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        // Heartbeat: no retries on purpose. A failed beat must fail fast so it
        // cannot hold the job Running across the next tick (JobService skips
        // Running jobs); the next beat 15s later covers any single miss.
        [Job("0/15 * * * * *")]
        public async Task Send() {
            try {
                var response = await httpClient.GetAsync("https://uptime.dev.sglade.com/api/push/yIc6q6ocjd?status=up&msg=OK");

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