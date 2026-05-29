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
        // Returns one base64 JPEG (no data: header) per page, or null on error.
        public static async Task<List<string>> AfxSetsB64(EggIncAccount account) {
            var posted = new AfxSetsAPIObject { EID = account.Id, Config = new AfxSetsCreatorConfig(100) };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("authenticationKey", DockerSecretsHelper.BotToken);

#if RELEASE
            var baseUrl = "https://egg9000.com";
#else
            var baseUrl = "https://egg9000.dev.sglade.com";
#endif
            var apiUrl = $"{baseUrl}/api/generateafxsetsb64";
            var content = new StringContent(JsonSerializer.Serialize(posted), Encoding.UTF8, "application/json");

            try {
                var response = await client.PostAsync(apiUrl, content);
                if(!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync();
                var parsed = JsonSerializer.Deserialize<AfxSetsB64Response>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return parsed?.Pages;
            } catch(Exception) {
                return null;
            }
        }
    }
}
