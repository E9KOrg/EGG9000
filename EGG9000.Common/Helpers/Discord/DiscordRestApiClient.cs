using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace EGG9000.Common.Helpers.Discord {
    /// <summary>
    /// Enhanced Discord REST API client for direct API calls.
    /// Supports easy token management via Discord.Net instances.
    /// </summary>
    public class DiscordRestApiClient : IDisposable {
        private readonly HttpClient _httpClient;
        private readonly string _token;
        private readonly ulong? _applicationId;
        private const string DiscordApiBaseUrl = "https://discord.com/api/v10";

        /// <summary>
        /// Initialize the REST client with a bot token directly.
        /// </summary>
        public DiscordRestApiClient(string token) {
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _httpClient = new HttpClient();
            ConfigureHttpClient();
        }

        /// <summary>
        /// Initialize the REST client by extracting the token and application ID from a DiscordSocketClient.
        /// </summary>
        public DiscordRestApiClient(DiscordSocketClient client) {
            if (client == null) throw new ArgumentNullException(nameof(client));
            
            // Extract token from the client's internal state
            _token = client.TokenValue ?? throw new InvalidOperationException("Discord client has no token set. Ensure the client is logged in before creating the REST client.");
            
            // Extract application ID from the current user
            if (client.CurrentUser != null) {
                _applicationId = client.CurrentUser.Id;
            }
            
            _httpClient = new HttpClient();
            ConfigureHttpClient();
        }

        /// <summary>
        /// Initialize the REST client with both token and application ID.
        /// </summary>
        public DiscordRestApiClient(string token, ulong applicationId) {
            _token = token ?? throw new ArgumentNullException(nameof(token));
            _applicationId = applicationId;
            _httpClient = new HttpClient();
            ConfigureHttpClient();
        }

        private void ConfigureHttpClient() {
            _httpClient.BaseAddress = new Uri(DiscordApiBaseUrl);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bot {_token}");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DiscordRestApiClient/1.0");
        }

        /// <summary>
        /// Get the stored application ID. Throws if not available.
        /// </summary>
        private ulong GetApplicationId() {
            if (_applicationId == null) {
                throw new InvalidOperationException("Application ID is not available. Initialize the client with a DiscordSocketClient or provide the application ID explicitly.");
            }
            return _applicationId.Value;
        }

        #region GET Methods

        /// <summary>
        /// Get a user by ID.
        /// </summary>
        public async Task<T> GetUserAsync<T>(ulong userId) {
            try {
                var response = await _httpClient.GetAsync($"/users/{userId}");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get user {userId}", ex);
            }
        }

        /// <summary>
        /// Get a guild by ID.
        /// </summary>
        public async Task<T> GetGuildAsync<T>(ulong guildId) {
            try {
                var response = await _httpClient.GetAsync($"/guilds/{guildId}");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get guild {guildId}", ex);
            }
        }

        /// <summary>
        /// Get a channel by ID.
        /// </summary>
        public async Task<T> GetChannelAsync<T>(ulong channelId) {
            try {
                var response = await _httpClient.GetAsync($"/channels/{channelId}");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get channel {channelId}", ex);
            }
        }

        /// <summary>
        /// Get a message by channel ID and message ID.
        /// </summary>
        public async Task<T> GetMessageAsync<T>(ulong channelId, ulong messageId) {
            try {
                var response = await _httpClient.GetAsync($"/channels/{channelId}/messages/{messageId}");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get message {messageId} from channel {channelId}", ex);
            }
        }

        /// <summary>
        /// Get multiple messages from a channel.
        /// </summary>
        public async Task<T> GetMessagesAsync<T>(ulong channelId, string queryParameters = null) {
            try {
                var endpoint = $"/channels/{channelId}/messages";
                if (!string.IsNullOrEmpty(queryParameters)) {
                    endpoint += $"?{queryParameters}";
                }
                var response = await _httpClient.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get messages from channel {channelId}", ex);
            }
        }

        /// <summary>
        /// Get guild members.
        /// </summary>
        public async Task<T> GetGuildMembersAsync<T>(ulong guildId, string queryParameters = null) {
            try {
                var endpoint = $"/guilds/{guildId}/members";
                if (!string.IsNullOrEmpty(queryParameters)) {
                    endpoint += $"?{queryParameters}";
                }
                var response = await _httpClient.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get members for guild {guildId}", ex);
            }
        }

        #endregion

        #region POST Methods

        /// <summary>
        /// Send a message to a channel.
        /// </summary>
        public async Task<T> SendMessageAsync<T>(ulong channelId, object payload) {
            try {
                var response = await _httpClient.PostAsJsonAsync($"/channels/{channelId}/messages", payload);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to send message to channel {channelId}", ex);
            }
        }

        /// <summary>
        /// Create a webhook in a channel.
        /// </summary>
        public async Task<T> CreateWebhookAsync<T>(ulong channelId, object payload) {
            try {
                var response = await _httpClient.PostAsJsonAsync($"/channels/{channelId}/webhooks", payload);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to create webhook in channel {channelId}", ex);
            }
        }

        #endregion

        #region PATCH Methods

        /// <summary>
        /// Edit a message.
        /// </summary>
        public async Task<T> EditMessageAsync<T>(ulong channelId, ulong messageId, object payload) {
            try {
                var response = await _httpClient.PatchAsync($"/channels/{channelId}/messages/{messageId}", 
                    new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), 
                    System.Text.Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to edit message {messageId} in channel {channelId}", ex);
            }
        }

        /// <summary>
        /// Edit a guild member.
        /// </summary>
        public async Task<T> EditGuildMemberAsync<T>(ulong guildId, ulong memberId, object payload) {
            try {
                var response = await _httpClient.PatchAsync($"/guilds/{guildId}/members/{memberId}", 
                    new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), 
                    System.Text.Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to edit member {memberId} in guild {guildId}", ex);
            }
        }

        #endregion

        #region DELETE Methods

        /// <summary>
        /// Delete a message.
        /// </summary>
        public async Task DeleteMessageAsync(ulong channelId, ulong messageId) {
            try {
                var response = await _httpClient.DeleteAsync($"/channels/{channelId}/messages/{messageId}");
                response.EnsureSuccessStatusCode();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to delete message {messageId} from channel {channelId}", ex);
            }
        }

        #endregion

        #region Custom Endpoints

        /// <summary>
        /// Make a custom GET request to any Discord API endpoint.
        /// </summary>
        public async Task<T> CustomGetAsync<T>(string endpoint) {
            try {
                var response = await _httpClient.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get from endpoint {endpoint}", ex);
            }
        }

        /// <summary>
        /// Make a custom POST request to any Discord API endpoint.
        /// </summary>
        public async Task<T> CustomPostAsync<T>(string endpoint, object payload) {
            try {
                var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to post to endpoint {endpoint}", ex);
            }
        }

        /// <summary>
        /// Make a custom PATCH request to any Discord API endpoint.
        /// </summary>
        public async Task<T> CustomPatchAsync<T>(string endpoint, object payload) {
            try {
                var response = await _httpClient.PatchAsync(endpoint, 
                    new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), 
                    System.Text.Encoding.UTF8, "application/json"));
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to patch endpoint {endpoint}", ex);
            }
        }

        /// <summary>
        /// Make a custom DELETE request to any Discord API endpoint.
        /// </summary>
        public async Task CustomDeleteAsync(string endpoint) {
            try {
                var response = await _httpClient.DeleteAsync(endpoint);
                response.EnsureSuccessStatusCode();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to delete from endpoint {endpoint}", ex);
            }
        }

        #endregion

        /// <summary>
        /// Edit permissions for an application command in a guild using the stored application ID.
        /// PUT /applications/{application.id}/guilds/{guild.id}/commands/{command.id}/permissions
        /// </summary>
        /// <param name="guildId">The ID of the guild</param>
        /// <param name="commandId">The ID of the command</param>
        /// <param name="permissions">The permission payload containing the permissions array</param>
        /// <returns>The updated Application Command Permissions object</returns>
        public async Task<T> EditApplicationCommandPermissionsAsync<T>(ulong guildId, ulong commandId, object permissions) {
            try {
                var applicationId = GetApplicationId();
                var endpoint = $"/applications/{applicationId}/guilds/{guildId}/commands/{commandId}/permissions";
                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(permissions), 
                    System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync(endpoint, content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
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
        public async Task<T> GetApplicationCommandPermissionsAsync<T>(ulong guildId, ulong commandId) {
            try {
                var applicationId = GetApplicationId();
                var endpoint = $"/applications/{applicationId}/guilds/{guildId}/commands/{commandId}/permissions";
                var response = await _httpClient.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            } catch (HttpRequestException ex) {
                throw new DiscordRestException($"Failed to get permissions for command {commandId} in guild {guildId}", ex);
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
}