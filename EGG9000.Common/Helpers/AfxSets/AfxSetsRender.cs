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
    public class AfxSetsCreatorConfig : IRenderConfig {
        // Shared default so the bot's set-selection dropdown pages the same way the renderer does.
        public const int DefaultSetsPerPage = 5;

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
            SetsPerPage = DefaultSetsPerPage;
            SlotsPerRow = 4;
        }

        public bool IsValid(out string error) {
            if(AFSize <= 0) { error = "AFSize must be > 0."; return false; }
            if(Padding < 0) { error = "Padding must be >= 0."; return false; }
            if(StoneSize <= 0) { error = "StoneSize must be > 0."; return false; }
            if(AFCornerRadius < 0) { error = "AFCornerRadius must be >= 0."; return false; }
            if(LabelWidth < 0) { error = "LabelWidth must be >= 0."; return false; }
            if(TextFontSize <= 0) { error = "TextFontSize must be > 0."; return false; }
            if(SetsPerPage <= 0) { error = "SetsPerPage must be > 0."; return false; }
            if(SlotsPerRow <= 0) { error = "SlotsPerRow must be > 0."; return false; }
            error = null;
            return true;
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

            var siteApi = SiteApiClient.Create();
            using var client = siteApi.client;
            var baseUrl = siteApi.baseUrl;

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
