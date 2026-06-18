using System;
using System.Net.Http;

namespace EGG9000.Common.Helpers {
    // Bot -> Site internal API access. In prod the bot reaches the co-located Site over the Docker
    // network (E9K_SITE_BASEURL=http://site:5013) instead of the public domain, which a
    // container cannot reliably hairpin back to. Falls back to the public URL when the override is unset.
    public static class SiteApiClient {
        public static string BaseUrl() {
            var baseUrl = Environment.GetEnvironmentVariable("E9K_SITE_BASEURL");
            if(string.IsNullOrWhiteSpace(baseUrl)) {
#if RELEASE
                baseUrl = "https://egg9000.com";
#else
                baseUrl = "https://egg9000.dev.sglade.com";
#endif
            }
            return baseUrl.TrimEnd('/');
        }

        // Client targeting the Site API with the bot auth header set. Accepts the dev self-signed
        // cert only when the resolved host is localhost (matched on parsed host, not a substring).
        public static (string baseUrl, HttpClient client) Create() {
            var baseUrl = BaseUrl();
            var isLocalHost = Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)
                && (parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || parsed.Host == "127.0.0.1");
            HttpClientHandler handler = isLocalHost ? new HttpClientHandler {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            } : null;
            var client = handler is null ? new HttpClient() : new HttpClient(handler);
            client.DefaultRequestHeaders.Add("authenticationKey", SecretsHelper.BotToken);
            return (baseUrl, client);
        }
    }
}
