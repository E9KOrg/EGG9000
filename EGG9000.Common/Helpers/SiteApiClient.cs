using System;
using System.Net.Http;

namespace EGG9000.Common.Helpers {
    public static class SiteApiClient {
        private static readonly Lazy<HttpClient> _client = new(Build);

        public static HttpClient Client => _client.Value;

        public static string BaseUrl() {
            var baseUrl = Environment.GetEnvironmentVariable("E9K_SITE_BASEURL");
            if(string.IsNullOrWhiteSpace(baseUrl)) {
                baseUrl = BuildConfig.IsRelease ? "https://egg9000.com" : "https://egg9000.dev.sglade.com";
            }
            return baseUrl.TrimEnd('/');
        }

        private static HttpClient Build() {
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
            if(Uri.TryCreate(BaseUrl(), UriKind.Absolute, out var parsed)
                && (parsed.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || parsed.Host == "127.0.0.1")) {
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            }
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("authenticationKey", SecretsHelper.BotToken);
            return client;
        }
    }
}
