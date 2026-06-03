using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace EGG9000.Common.Helpers
{
    /// <summary>
    /// Helper to read secrets from Docker Secrets (/run/secrets/) or fall back to configuration.
    /// Supports both development (user secrets) and production (Docker secrets) workflows.
    /// </summary>
    public static class SecretsHelper
    {
        private const string SecretsPath = "/run/secrets";

        /// <summary>
        /// Read a secret from Docker Secrets file system.
        /// Returns null if file doesn't exist.
        /// </summary>
        public static string ReadSecret(string secretName)
        {
            var secretPath = Path.Combine(SecretsPath, secretName);
            if (File.Exists(secretPath))
            {
                return File.ReadAllText(secretPath).Trim();
            }
            return null;
        }

        /// <summary>
        /// Get value from Docker secret first, then fall back to configuration.
        /// </summary>
        /// <param name="config">Configuration instance</param>
        /// <param name="configKey">Configuration key (e.g., "ConnectionStrings:DefaultConnection")</param>
        /// <param name="secretName">Docker secret name (defaults to sanitized config key)</param>
        public static string GetConfigOrSecret(IConfiguration config, string configKey, string secretName = null)
        {
            // Use provided secret name or convert config key to secret name format
            var actualSecretName = secretName ?? configKey.Replace(":", "_").Replace("__", "_").ToLower();

            // Try Docker secret first (production)
            var secret = ReadSecret(actualSecretName);
            if (!string.IsNullOrEmpty(secret))
            {
                return secret;
            }

            // Fall back to configuration (development/user secrets)
            return config[configKey];
        }

        /// <summary>
        /// Check if running in Docker with secrets available
        /// </summary>
        public static bool IsDockerSecretsAvailable()
        {
            return Directory.Exists(SecretsPath);
        }

        public static string BotToken { get; private set; }

        /// <summary>
        /// Egg Inc API authentication passphrase. Loaded from the Docker secret
        /// <c>egg_inc_api_salt</c> or the <c>ConnectionStrings:ApiSalt</c> configuration key.
        /// Null/empty when not configured - authenticated Egg Inc endpoints then degrade gracefully.
        /// </summary>
        public static string ApiSalt { get; private set; }

        public static void Initialize(IConfiguration config)
        {
            BotToken = GetConfigOrSecret(config, "ConnectionStrings:Token", "token");
            ApiSalt = GetConfigOrSecret(config, "ConnectionStrings:ApiSalt", "egg_inc_api_salt");
        }
    }
}