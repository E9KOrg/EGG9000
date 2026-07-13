using Discord.WebSocket;
using System;

namespace EGG9000.Common.Helpers.Discord {
    /// <summary>
    /// Extension methods to simplify creating REST clients from Discord.Net instances.
    /// </summary>
    public static class DiscordRestExtensions {
        /// <summary>
        /// Create a Discord REST API client from a DiscordSocketClient instance.
        /// Automatically extracts the token for authentication.
        /// </summary>
        public static DiscordRestApiClient CreateRestClient(this DiscordSocketClient client) {
            if (client == null) throw new ArgumentNullException(nameof(client));
            return new DiscordRestApiClient(client);
        }

        /// <summary>
        /// Create a Discord REST API client from a token string.
        /// </summary>
        public static DiscordRestApiClient CreateRestClient(string token) {
            return new DiscordRestApiClient(token);
        }
    }
}