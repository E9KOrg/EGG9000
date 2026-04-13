using Docker.DotNet;
using Docker.DotNet.Models;

using EGG9000.Common.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Site.Services {
    public class DockerCheckService : BackgroundService {
        private readonly ILogger<DockerCheckService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(0.5);
        private readonly IConfiguration _config;
        private string DockerHost => _config["Docker:Host"] ?? "npipe://./pipe/docker_engine";

        public DockerCheckService(ILogger<DockerCheckService> logger, IConfiguration config, ActiveMonitorHostedService activeMonitorHostedService) {
            _logger = logger;
            _config = config;

            _ = activeMonitorHostedService.SetActiveColorAsync();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
#if RELEASE
            using var timer = new PeriodicTimer(_interval);

            _logger.LogInformation("Interval service started.");

            while(await timer.WaitForNextTickAsync(stoppingToken)) {
                try {
                    await DoWorkAsync();
                } catch(Exception ex) {
                    _logger.LogError(ex, "Error during interval task execution.");
                }
            }
#endif
        }

        private async Task DoWorkAsync() {
            _logger.LogTrace("DockerController timer tick");
            var client = new DockerClientConfiguration(new Uri(DockerHost)).CreateClient();
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters() {
                All = true
            });
            _logger.LogInformation("Found {count} containers", containers.Count);

            var botContainers = containers.Where(c => c.Names.Any(n => n.Contains("egg9000-bot"))).ToList();

            var botBlue = botContainers.FirstOrDefault(c => c.Names.Any(n => n.Contains("blue")));
            var botGreen = botContainers.FirstOrDefault(c => c.Names.Any(n => n.Contains("green")));
            var botBlueRunning = botBlue is not null && botBlue.State == "running";
            var botGreenRunning = botGreen is not null && botGreen.State == "running";

            if(botBlue is not null && !botBlueRunning && botGreenRunning) {
                _logger.LogInformation("Removing stopped bot container blue");
                await client.Containers.RemoveContainerAsync(botBlue.ID, new ContainerRemoveParameters() { Force = true, RemoveVolumes = true });
            } else if(botGreen is not null && !botGreenRunning && botBlueRunning) {
                _logger.LogInformation("Removing stopped bot container green");
                await client.Containers.RemoveContainerAsync(botGreen.ID, new ContainerRemoveParameters() { Force = true, RemoveVolumes = true });
            } else if(!botBlueRunning && !botGreenRunning) {
                var blueState = botBlue?.State ?? "non-existent";
                var greenState = botGreen?.State ?? "non-existent";
                _logger.LogInformation("Both bot containers are stopped or don't exist! Blue: {blueState}, Green: {greenState}", blueState, greenState);
            }
        }
    }
}
