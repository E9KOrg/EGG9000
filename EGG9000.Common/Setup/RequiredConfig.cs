using EGG9000.Common.Helpers;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EGG9000.Common.Setup {
    // How strictly a configuration key is needed.
    public enum ConfigRequirement {
        // Needed in every build configuration.
        Always,
        // Needed only in RELEASE. Absent in dev is normal and not an error.
        ReleaseOnly,
        // Never blocks startup. Enables an extra feature when present.
        Optional
    }

    // Which application reads a configuration key.
    public enum ConfigComponent { Bot, Site, Both }

    // ConfigKey: Configuration key, for example "ConnectionStrings:Token". DockerSecretName: Docker secret
    // filename under /run/secrets, or null when the secret filename should be derived from the config key.
    // Component: Which application reads it. Requirement: How strictly it is needed. Purpose: One line
    // explaining what breaks without it. Printed by onboard preflight.
    public sealed record ConfigEntry(
        string ConfigKey,
        string DockerSecretName,
        ConfigComponent Component,
        ConfigRequirement Requirement,
        string Purpose);

    // The single source of truth for which configuration keys this solution needs. The onboard preflight
    // reflects this list instead of the README enumerating keys by hand, so a new or renamed secret cannot
    // silently leave setup documentation stale.
    public static class RequiredConfig {
        private static readonly List<ConfigEntry> _all = [
            new("ConnectionStrings:DefaultConnection", "db_connection_string", ConfigComponent.Both, ConfigRequirement.Always,
                "PostgreSQL connection string. Nothing works without it."),
            new("ConnectionStrings:Token", "token", ConfigComponent.Bot, ConfigRequirement.Always,
                "Discord bot token. The bot cannot log in without it."),
            new("ConnectionStrings:ClientId", null, ConfigComponent.Site, ConfigRequirement.Always,
                "Discord application client id for site OAuth login."),
            new("ConnectionStrings:ClientSecret", null, ConfigComponent.Site, ConfigRequirement.Always,
                "Discord application client secret for site OAuth login."),
            new("ConnectionStrings:ApiSalt", "egg_inc_api_salt", ConfigComponent.Both, ConfigRequirement.ReleaseOnly,
                "Egg Inc API passphrase. Authenticated API endpoints are silently disabled without it."),
            new("ConnectionStrings:RabbitMQServer", "rabbitmq_connection", ConfigComponent.Both, ConfigRequirement.ReleaseOnly,
                "RabbitMQ connection as host|user|pass. DEBUG falls back to the in-memory transport."),
            new("ConnectionStrings:BugsInkURL", "bugsink_url", ConfigComponent.Both, ConfigRequirement.Optional,
                "Sentry/Bugsink DSN for error reporting."),
            new("ConnectionStrings:BugSnagApiKey", "bugsnag_api_key", ConfigComponent.Both, ConfigRequirement.ReleaseOnly,
                "Bugsnag error monitoring API key. Read only in RELEASE builds."),
            new("ConnectionStrings:BusControlSecret", "bus_control_secret", ConfigComponent.Both, ConfigRequirement.Optional,
                "Shared secret for bus control endpoints. Absent disables bus auth enforcement."),
            new("ConnectionStrings:CPGuildId", null, ConfigComponent.Bot, ConfigRequirement.Optional,
                "Primary guild id used to prioritise coop processing."),
            new("ConnectionStrings:DevGuildId", "dev_guild_id", ConfigComponent.Both, ConfigRequirement.Optional,
                "Your own test Discord server id. Setup writes this when you pick a server; unset falls back to the shared E9K dev server."),
            new("DataProtection:CertPath", "dataprotection_cert_path", ConfigComponent.Site, ConfigRequirement.ReleaseOnly,
                "Certificate protecting ASP.NET data-protection keys. Without it the keys are persisted unencrypted."),
            new("DataProtection:CertPassword", "dataprotection_cert_password", ConfigComponent.Site, ConfigRequirement.ReleaseOnly,
                "Password for the data-protection certificate.")
        ];

        public static IReadOnlyList<ConfigEntry> All => _all;

        // Entries that the given component needs but cannot currently resolve, from either a Docker secret
        // or configuration. Optional entries are never returned. ReleaseOnly entries are returned only when
        // isRelease is true. Passing ConfigComponent.Both means "every component", not "only the entries
        // tagged Both", so a caller asking what the whole solution needs gets all of it.
        public static IReadOnlyList<ConfigEntry> MissingFor(IConfiguration config, ConfigComponent component, bool isRelease) {
            return [.. _all
                .Where(e => component == ConfigComponent.Both
                         || e.Component == component
                         || e.Component == ConfigComponent.Both)
                .Where(e => e.Requirement == ConfigRequirement.Always
                         || (e.Requirement == ConfigRequirement.ReleaseOnly && isRelease))
                .Where(e => string.IsNullOrWhiteSpace(
                    SecretsHelper.GetConfigOrSecret(config, e.ConfigKey, e.DockerSecretName)))];
        }

        // Best-effort path where .NET looks for secrets.json on this machine, for error messages. Windows
        // uses APPDATA; Unix uses ~/.microsoft/usersecrets.
        public static string UserSecretsPathHint(string userSecretsId) {
            var appData = Environment.GetEnvironmentVariable("APPDATA");
            if(!string.IsNullOrEmpty(appData)) {
                return Path.Combine(appData, "Microsoft", "UserSecrets", userSecretsId, "secrets.json");
            }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".microsoft", "usersecrets", userSecretsId, "secrets.json");
        }
    }
}
