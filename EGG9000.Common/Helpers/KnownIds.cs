using Microsoft.Extensions.Configuration;

namespace EGG9000.Common.Helpers {
    // Well-known Discord snowflakes that were previously hardcoded across the codebase.
    // Swapping a raw literal for one of these consts is value-preserving in every build config.
    public static class KnownGuilds {
        // Configuration key holding the developer's own test server id.
        public const string DevGuildConfigKey = "ConnectionStrings:DevGuildId";

        // The real Palace server. A production constant, not a development one.
        public const ulong PalaceProduction = 656455567858073601;

        // The E9K dev server. Only a default: a developer without access to that server's bot token
        // sets DevGuildConfigKey to their own server instead, which setup writes for them. Keeping
        // the literal as the fallback means every existing checkout behaves exactly as before.
        private const ulong DefaultDev = 1108127105088241746;

        private static ulong _dev = DefaultDev;

        // The test server this instance uses. Configurable so anyone can run the bot in a server of their
        // own rather than needing the token of the bot that lives in the shared dev server.
        public static ulong Dev => _dev;

        // The Palace home guild. Under the DEV9002 config the bot points at the dev guild instead.
        public static ulong Palace => BuildConfig.IsDev9002 ? _dev : PalaceProduction;

        // True when this instance is using a developer's own server rather than the default.
        public static bool DevGuildIsConfigured => _dev != DefaultDev;

        // Reads the dev server id from configuration once at startup. Absent, unparseable or zero leaves
        // the default in place, so an existing setup keeps working untouched.
        public static void Initialize(IConfiguration configuration) {
            var raw = SecretsHelper.GetConfigOrSecret(configuration, DevGuildConfigKey, "dev_guild_id");
            if(ulong.TryParse(raw, out var id) && id != 0) {
                _dev = id;
            }
        }
    }

    public static class KnownRoles {
        public const ulong Overflow = 775547850134257675;
        public const ulong Registered = 794713762396897280;
        public const ulong Unjoined = 796512753241161748;
        public const ulong Active = 798284088967430144;
    }

    public static class KnownUsers {
        public const ulong Bot = 514257192803893272;
    }
}
