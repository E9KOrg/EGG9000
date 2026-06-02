using EGG9000.Common.Helpers;

namespace EGG9000.Common.EggIncAPI {

    /// <summary>
    /// Access to the Egg Inc API authentication passphrase ("salt").
    /// The value lives in a runtime secret, never in source. When it is absent the only affected
    /// path is request signing for <c>ei_ctx/get_contract_player_info</c> and the legacy
    /// <c>ei/first_contact_secure</c> fallback; everything else works without it.
    /// </summary>
    public static class EggIncApiSecrets {
        /// <summary>The passphrase, or null/empty when not configured.</summary>
        public static string Salt => DockerSecretsHelper.ApiSalt;

        /// <summary>True when a salt is configured and request signing is possible.</summary>
        public static bool IsSaltAvailable => !string.IsNullOrEmpty(Salt);
    }
}
