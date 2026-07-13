using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

namespace EGG9000.Common.Helpers.Discord {
    /// <summary>
    /// Enhanced Discord REST API client for direct API calls.
    /// Supports easy token management via Discord.Net instances.
    /// </summary>
    public class DiscordRestApiClient : IDisposable {
        private readonly HttpClient _httpClient;
        private readonly ulong? _applicationId;
        private readonly string _botToken;
        private const string DiscordApiBaseUrl = "https://discord.com/api/";


        public DiscordRestApiClient(DiscordSocketClient client) {
            if(client == null) throw new ArgumentNullException(nameof(client));

            if(client.CurrentUser != null) {
                _applicationId = client.CurrentUser.Id;
                _botToken = SecretsHelper.BotToken;
            }

            

            _httpClient = new HttpClient();
            ConfigureHttpClient();
        }

        private void ConfigureHttpClient() {
            _httpClient.BaseAddress = new Uri(DiscordApiBaseUrl);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordRestApiClient/1.0");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _botToken);
        }

        public class LowercaseNamingPolicy : JsonNamingPolicy {
            public override string ConvertName(string name) => name.ToLower();
        }

        private static void AddBearerAuth(HttpRequestMessage request, string accessToken) {
            if(string.IsNullOrWhiteSpace(accessToken)) {
                throw new ArgumentNullException(nameof(accessToken));
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        private ulong GetApplicationId() {
            if(_applicationId == null) {
                throw new InvalidOperationException("Application ID is not available. Initialize with DiscordSocketClient or provide application ID explicitly.");
            }
            return _applicationId.Value;
        }

      
        /// <summary>
        /// Edit permissions for an application command in a guild using the stored application ID.
        /// PUT /applications/{application.id}/guilds/{guild.id}/commands/{command.id}/permissions
        /// </summary>
        /// <param name="guildId">The ID of the guild</param>
        /// <param name="commandId">The ID of the command</param>
        /// <param name="permissions">The permission payload containing the permissions array</param>
        /// <param name="accessToken">The access token for authentication</param>
        /// <returns>The updated Application Command Permissions object</returns>

        public async Task<GuildApplicationCommandPermissionRest> EditApplicationCommandPermissionsAsync(ulong guildId, ulong commandId, object permissions, string accessToken) {
            try {
                var applicationId = GetApplicationId();
                var endpoint = $"applications/{applicationId}/guilds/{guildId}/commands/{commandId}/permissions";
                var json = JsonSerializer.Serialize(permissions);

                using var request = new HttpRequestMessage(HttpMethod.Put, endpoint) {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                AddBearerAuth(request, accessToken);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<GuildApplicationCommandPermissionRest>();
            } catch(HttpRequestException ex) {
                throw new DiscordRestException($"Failed to edit permissions for command {commandId} in guild {guildId}", ex);
            }
        }

        /// <summary>
        /// Get permissions for an application command in a guild using the stored application ID.
        /// GET /applications/{application.id}/guilds/{guild.id}/commands/{command.id}/permissions
        /// </summary>
        /// <param name="guildId">The ID of the guild</param>
        /// <param name="commandId">The ID of the command</param>
        /// <returns>The Application Command Permissions object</returns>
        public async Task<List<GuildApplicationCommandPermissionRest>> GetApplicationCommandPermissionsAsync(ulong guildId) {
            try {
                var applicationId = GetApplicationId();
                var endpoint = $"applications/{applicationId}/guilds/{guildId}/commands/permissions";
                var response = await _httpClient.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<GuildApplicationCommandPermissionRest>>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get permissions for commands in guild {guildId} and application {GetApplicationId()}", ex);
            }
        }

        public void Dispose() {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Custom exception for Discord REST API errors.
    /// </summary>
    public class DiscordRestException : Exception {
        public DiscordRestException(string message) : base(message) { }
        public DiscordRestException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class GuildApplicationCommandPermissionRest {
        [JsonPropertyName("id")]
        public ulong Id { get; set; }
        [JsonPropertyName("application_id")]
        public ulong ApplicationId { get; set; }
        [JsonPropertyName("guild_id")]
        public ulong GuildId { get; set; }
        [JsonPropertyName("permissions")]
        public List<CommandPermissionRest> Permissions { get; set; } = new List<CommandPermissionRest>();
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