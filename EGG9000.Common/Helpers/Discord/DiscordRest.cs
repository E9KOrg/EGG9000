using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers.Discord {

    /// <summary>
    /// Thin Discord REST helpers (not part of the Egg Inc API). Used for the small number of
    /// raw Discord HTTP calls that Discord.Net does not surface conveniently.
    /// </summary>
    public static class DiscordRest {

        public static async Task<T> GetAsBot<T>(string path, string discordToken) {
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://discordapp.com/api/");
            client.DefaultRequestHeaders.Add("Authorization", "Bot " + discordToken);
            var response = await client.GetAsync(path);
            await Task.Delay(500);
            return await response.Content.ReadFromJsonAsync<T>();
        }

        public static async Task<T> PutAsUser<T, U>(string path, string discordToken, U @params) {
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://discordapp.com/api/");
            client.DefaultRequestHeaders.Add("Authorization", "Bearer " + discordToken);
            var response = await client.PutAsJsonAsync(path, @params);
            await Task.Delay(500);
            return await response.Content.ReadFromJsonAsync<T>();
        }
    }
}
