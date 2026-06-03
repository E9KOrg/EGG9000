using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

namespace EGG9000.Common.Helpers.AfxSets {
    // Sizing + layout config shared between the bot (sender) and site (renderer).
    public class AfxSetsCreatorConfig {
        public int AFSize { get; set; }
        public int Padding { get; set; }
        public int StoneSize { get; set; }
        public int AFCornerRadius { get; set; }
        public int LabelWidth { get; set; }
        public int TextFontSize { get; set; }
        public int SetsPerPage { get; set; }
        public int SlotsPerRow { get; set; }

        public AfxSetsCreatorConfig() { } // for JSON deserialization

        public AfxSetsCreatorConfig(int afSize) {
            AFSize = afSize;
            Padding = afSize / 5;
            StoneSize = (int)(afSize / 4.5);
            AFCornerRadius = afSize / 4;
            LabelWidth = (int)(afSize * 1.1);
            TextFontSize = afSize / 4;
            SetsPerPage = 5;
            SlotsPerRow = 4;
        }
    }

    public class AfxSetsAPIObject {
        public string EID { get; set; }
        public AfxSetsCreatorConfig Config { get; set; }
    }

    public class AfxSetsB64Response {
        public List<string> Pages { get; set; } = new();
    }

    public static class AfxSetsRender {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        // Returns one base64 JPEG (no data: header) per page. On failure pages is null and error
        // holds a short human-readable reason (also logged).
        public static async Task<(List<string> pages, string error)> AfxSetsB64(EggIncAccount account) {
            var posted = new AfxSetsAPIObject { EID = account.Id, Config = new AfxSetsCreatorConfig(100) };

            // E9K_SITE_BASEURL overrides the target site (e.g. https://localhost:44314 when testing
            // against a locally-run Site through Visual Studio).
            var baseUrl = Environment.GetEnvironmentVariable("E9K_SITE_BASEURL");
            if(string.IsNullOrWhiteSpace(baseUrl)) {
#if RELEASE
                baseUrl = "https://egg9000.com";
#else
                baseUrl = "https://egg9000.dev.sglade.com";
#endif
            }
            baseUrl = baseUrl.TrimEnd('/');

            // A locally-run Site uses a self-signed dev cert; accept it only when targeting localhost.
            HttpClientHandler handler = null;
            if(baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) || baseUrl.Contains("127.0.0.1")) {
                handler = new HttpClientHandler {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
            }
            using var client = handler is null ? new HttpClient() : new HttpClient(handler);
            client.DefaultRequestHeaders.Add("authenticationKey", SecretsHelper.BotToken);

            var apiUrl = $"{baseUrl}/api/generateafxsetsb64";
            var content = new StringContent(JsonSerializer.Serialize(posted), Encoding.UTF8, "application/json");

            _logger.Info($"AfxSetsB64: POST {apiUrl} for EID {account.Id}");

            try {
                var response = await client.PostAsync(apiUrl, content);
                var json = await response.Content.ReadAsStringAsync();
                if(!response.IsSuccessStatusCode) {
                    var body = json.Length > 500 ? json[..500] : json;
                    var err = $"HTTP {(int)response.StatusCode} {response.StatusCode} from {apiUrl}\n{body}";
                    _logger.Warn($"AfxSetsB64: {err}");
                    return (null, err);
                }
                var parsed = JsonSerializer.Deserialize<AfxSetsB64Response>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if(parsed?.Pages is null || parsed.Pages.Count == 0) {
                    _logger.Warn("AfxSetsB64: response parsed but contained no pages.");
                    return (null, $"Site returned no pages from {apiUrl}");
                }
                _logger.Info($"AfxSetsB64: got {parsed.Pages.Count} page(s) from {apiUrl}");
                return (parsed.Pages, null);
            } catch(Exception e) {
                _logger.Error(e, $"AfxSetsB64: request to {apiUrl} threw");
                return (null, $"{e.GetType().Name}: {e.Message} (target {apiUrl})");
            }
        }
    }
}
