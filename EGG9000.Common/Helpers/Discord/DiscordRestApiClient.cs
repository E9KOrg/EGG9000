using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers.Discord {
    public sealed class DiscordRestApiClient : IDisposable {
        private const string DiscordApiBaseUrl = "https://discord.com/api/";

        private readonly HttpClient _httpClient;
        private readonly ulong _applicationId;

        public DiscordRestApiClient(DiscordSocketClient client) {
            ArgumentNullException.ThrowIfNull(client);
            _applicationId = client.CurrentUser?.Id ?? throw new InvalidOperationException("DiscordSocketClient.CurrentUser is not available yet.");

            var botToken = SecretsHelper.BotToken;
            if(string.IsNullOrWhiteSpace(botToken))
                throw new InvalidOperationException("Bot token is not configured.");

            _httpClient = new HttpClient { BaseAddress = new Uri(DiscordApiBaseUrl) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EGG9000/1.0");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        }

        public async Task<List<GuildApplicationCommandPermissionRest>> GetApplicationCommandPermissionsAsync(ulong guildId) {
            try {
                var response = await _httpClient.GetAsync($"applications/{_applicationId}/guilds/{guildId}/commands/permissions");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<GuildApplicationCommandPermissionRest>>() ?? [];
            } catch(HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get command permissions in guild {guildId}", ex);
            }
        }

        public async Task<GuildApplicationCommandPermissionRest> EditApplicationCommandPermissionsAsync(ulong guildId, ulong commandId, IReadOnlyCollection<CommandPermissionRest> permissions, string accessToken) {
            if(string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("A user access token is required to edit command permissions.", nameof(accessToken));

            try {
                using var request = new HttpRequestMessage(HttpMethod.Put, $"applications/{_applicationId}/guilds/{guildId}/commands/{commandId}/permissions") {
                    Content = JsonContent.Create(new { permissions })
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<GuildApplicationCommandPermissionRest>();
            } catch(HttpRequestException ex) {
                throw new DiscordRestException($"Failed to edit permissions for command {commandId} in guild {guildId}", ex);
            }
        }

        public void Dispose() => _httpClient.Dispose();
    }

    public class DiscordRestException(string message, Exception innerException) : Exception(message, innerException);

    public class GuildApplicationCommandPermissionRest {
        [JsonPropertyName("id")]
        public ulong Id { get; set; }
        [JsonPropertyName("application_id")]
        public ulong ApplicationId { get; set; }
        [JsonPropertyName("guild_id")]
        public ulong GuildId { get; set; }
        [JsonPropertyName("permissions")]
        public List<CommandPermissionRest> Permissions { get; set; } = [];
    }

    public class CommandPermissionRest {
        [JsonPropertyName("id")]
        public ulong Id { get; set; }
        [JsonPropertyName("type")]
        public int Type { get; set; }
        [JsonPropertyName("permission")]
        public bool Permission { get; set; }
    }
}
