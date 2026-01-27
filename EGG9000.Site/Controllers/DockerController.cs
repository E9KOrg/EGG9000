using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Docker.DotNet;
using Docker.DotNet.Models;

using EGG9000.Common.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

namespace EGG9000.Site.Controllers {
    [Authorize(Roles = "Admin,GuildAdmin,GuildLesserAdmin")]
    public class DockerController : Controller {
        private readonly IConfiguration _config;
        private readonly ILogger<DockerController> _logger;
        private readonly IDbContextFactory<EGG9000.Common.Database.ApplicationDbContext> _dbFactory;

        private string DockerHost => _config["Docker:Host"] ?? "npipe://./pipe/docker_engine";

        public DockerController(IConfiguration config, ILogger<DockerController> logger, IDbContextFactory<EGG9000.Common.Database.ApplicationDbContext> dbFactory) {
            _config = config;
            _logger = logger;
            _dbFactory = dbFactory;
        }

        // Docker Hub posts JSON to this endpoint. Keep it AllowAnonymous if Docker Hub can't authenticate.
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook() {
            // Read raw request body
            string body;
            using (var sr = new StreamReader(Request.Body)) {
                body = await sr.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(body)) {
                _logger.LogWarning("Docker webhook called with empty body.");
                return BadRequest("empty body");
            }

            // Optional: validate a shared secret (set DockerHub:WebhookSecret in config or environment)
            var expectedSecret = _config["DockerHub:WebhookSecret"];
            if (!string.IsNullOrEmpty(expectedSecret)) {
                var provided = Request.Headers["X-Webhook-Token"].FirstOrDefault() ?? Request.Query["token"].FirstOrDefault();
                if (string.IsNullOrEmpty(provided) || provided != expectedSecret) {
                    _logger.LogWarning("Docker webhook authentication failed. Provided token missing or incorrect.");
                    return Unauthorized();
                }
            }

            // Parse JSON safely
            JsonDocument doc;
            try {
                doc = JsonDocument.Parse(body);
            } catch (System.Text.Json.JsonException ex) {
                _logger.LogWarning(ex, "Invalid JSON payload from Docker Hub");
                return BadRequest("invalid json");
            }

            var root = doc.RootElement;

            // Extract common Docker Hub webhook fields (guarded)
            string tag = null;
            if (root.TryGetProperty("push_data", out var pushData) && pushData.TryGetProperty("tag", out var tagElem))
                tag = tagElem.GetString();

            string repo = null;
            if (root.TryGetProperty("repository", out var repoElem) && repoElem.TryGetProperty("repo_name", out var repoName))
                repo = repoName.GetString();

            string pusher = null;
            if (pushData.ValueKind != JsonValueKind.Undefined && pushData.TryGetProperty("pusher", out var pusherElem))
                pusher = pusherElem.GetString();

            _logger.LogInformation("Docker Hub webhook received. repo={repo} tag={tag} pusher={pusher}", repo, tag, pusher);

            if(tag is not null) {
                await PullImage(tag);
            }

            return Ok(new { received = true, repository = repo, tag });
        }

        [AllowAnonymous]
        public async Task<IActionResult> PullImage(string tag) {
           var client = new DockerClientConfiguration(new Uri(DockerHost)).CreateClient();
            
            var progress = new Progress<JSONMessage>(m => {
                if(m.Status == "Pull complete") {
                    _logger.LogInformation("Docker image pull complete: {tag}", tag);
                }
            });

            await client.Images.CreateImageAsync(
                new Docker.DotNet.Models.ImagesCreateParameters {
                    FromImage = "kendrome/egg9000bot",
                    Tag = tag
                },
                null,
                progress);

            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters() {
                All = true
            });
            var botContainers = containers.Where(c => c.Names.Any(n => n.Contains("egg9000-bot"))).ToList();
            
            
            
            
            var botBlue = botContainers.FirstOrDefault(c => c.Names.Any(n => n.Contains("blue")));
            var botGreen = botContainers.FirstOrDefault(c => c.Names.Any(n => n.Contains("green")));

            await using var ctx = await _dbFactory.CreateDbContextAsync();
            var conn = ctx.Database.GetDbConnection();
            await conn.OpenAsync();


            var activeColor = await ActiveMonitorHostedService.ReadActiveColorAsync(conn, "bot", CancellationToken.None);

            switch(activeColor) {
                case "blue":
                    if(botBlue is not null) {
                        if(botGreen is not null) {
                            _logger.LogWarning("Removing old green bot container before updating blue.");
                            await client.Containers.RemoveContainerAsync(botGreen.ID, new ContainerRemoveParameters() { Force = true, RemoveVolumes = true });
                        }
                        await RecreateContainer(client, botBlue, $"kendrome/egg9000bot:{tag}", "blue", "green");
                    } else {
                        _logger.LogWarning("No blue bot container found to update.");
                    }
                    break;
                case "green":
                    if(botGreen is not null) {
                        if(botBlue is not null) {
                            _logger.LogWarning("Removing old blue bot container before updating green.");
                            await client.Containers.RemoveContainerAsync(botBlue.ID, new ContainerRemoveParameters() { Force = true, RemoveVolumes = true });
                        }
                        await RecreateContainer(client, botGreen, $"kendrome/egg9000bot:{tag}", "green", "blue");
                    } else {
                        _logger.LogWarning("No green bot container found to update.");
                    }
                    break;
                default:
                    _logger.LogWarning("Active color for bot service is unknown: {color}", activeColor);
                    break;
            }

            return Content("");
        }

        private async Task RecreateContainer(DockerClient client, ContainerListResponse containerInfo, string newImage, string oldColor, string newColor) {
            var info = await client.Containers.InspectContainerAsync(containerInfo.ID);
            //await client.Containers.RemoveContainerAsync(containerInfo.ID, new ContainerRemoveParameters() { Force = true, RemoveVolumes = false });
            var env = info.Config?.Env?.Select(x => x.Replace(oldColor, newColor)).ToList() ?? new();

            var labels = info.Config?.Labels.Select(x => new KeyValuePair<string, string>(x.Key, x.Value.Replace(oldColor, newColor))).ToDictionary() ?? new Dictionary<string, string>();

            var createParams = new CreateContainerParameters {
                Image = newImage,
                Name = containerInfo.Names.FirstOrDefault()?.TrimStart('/').Replace(oldColor, newColor),
                Env = env,
                Labels = labels,
                HostConfig = info.HostConfig
            };
            var newContainer = await client.Containers.CreateContainerAsync(createParams);
            var started = await client.Containers.StartContainerAsync(newContainer.ID, new ContainerStartParameters());
        }
        private async Task RecreateContainer(DockerClient client, ContainerListResponse containerInfo, string newImage) {
            var info = await client.Containers.InspectContainerAsync(containerInfo.ID);
            //await client.Containers.RemoveContainerAsync(containerInfo.ID, new ContainerRemoveParameters() { Force = true, RemoveVolumes = false });
            var env = info.Config?.Env?.ToList() ?? new();

            var labels = info.Config?.Labels.ToDictionary() ?? new Dictionary<string, string>();

            var createParams = new CreateContainerParameters {
                Image = newImage,
                Name = containerInfo.Names.FirstOrDefault()?.TrimStart('/'),
                Env = env,
                Labels = labels,
                HostConfig = info.HostConfig
            };
            var newContainer = await client.Containers.CreateContainerAsync(createParams);
            Console.WriteLine(JsonConvert.SerializeObject(newContainer, Formatting.Indented));
            var started = await client.Containers.StartContainerAsync(newContainer.ID, new ContainerStartParameters());
            Console.WriteLine(started);

            //TODO: Set BOT_ACTIVIE=false on the other container
        }
    }
}
