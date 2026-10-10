using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class EventHelpers {

        public static async Task<List<EventCustomization>> GetCustomizationsAsync(this ApplicationDbContext db, Guild dbGuild) {
            if(!db._cache.TryGetValue(dbGuild.GetECCacheKey(), out List<EventCustomization> customizations)) {
                customizations = (await db.Guilds.AsQueryable().FirstOrDefaultAsync(g => g.Id == dbGuild.Id)).EventCustomizations;
                db._cache.Set(dbGuild.GetECCacheKey(), customizations, TimeSpan.FromDays(1));
            }
            return customizations;
        }

        public static async Task<EventCustomization> GetCustomizationAsync(this ApplicationDbContext db, Guild dbguild, DBEvent customEvent) {
            return await db.GetCustomizationAsync(dbguild, customEvent.Type);
        }

        public static async Task<EventCustomization> GetCustomizationAsync(this ApplicationDbContext db, Guild dbguild, string eventType) {
            Guild palaceGuild;
            if(BuildConfig.IsDev9002) {
                palaceGuild = await db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == KnownGuilds.Dev);
            } else {
                palaceGuild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(x => x.DiscordSeverId == KnownGuilds.Palace);
                if(palaceGuild == null) {
                    palaceGuild = await db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == KnownGuilds.Dev);
                }
            }
            var gCustomizations = await db.GetCustomizationsAsync(dbguild);
            var pCustomizations = await db.GetCustomizationsAsync(palaceGuild);

            return gCustomizations.FirstOrDefault(ec => string.Equals(ec.Type, eventType, StringComparison.InvariantCultureIgnoreCase))
                ?? pCustomizations.FirstOrDefault(ec => string.Equals(ec.Type, eventType, StringComparison.InvariantCultureIgnoreCase));
        }

        public static string InvalidateEventCustomizations(this ApplicationDbContext db, Guild guild) {
            db._cache.Set(guild.GetECCacheKey(), new List<EventCustomization>(), TimeSpan.FromMilliseconds(1));
            return guild.GetECCacheKey();
        }

        private static string GetECCacheKey(this Guild g) {
            return "EventCustomizationCache:" + g.Id.ToString();
        }

        public static async Task<Image> GetEventImageAsync(this ApplicationDbContext db, DBEvent customEvent) {
            var eventKey = $"{customEvent.Type.ToLowerInvariant()}-U:{customEvent.CcOnly}";
            if(!db._cache.TryGetValue(eventKey, out Image image)) {
                image = await GenerateEventImageAsync(customEvent);
                if(image is null) return null;
                db._cache.Set(eventKey, image);
            }
            return image;
        }

        private static async Task<Image> GenerateEventImageAsync(DBEvent customEvent) {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("authenticationKey", SecretsHelper.BotToken);

            var baseUrl = BuildConfig.IsRelease ? "https://egg9000.com" : "https://localhost:44314";

            var apiUrl = $"{baseUrl}/api/generateeventimage";
            var jsonContent = JsonSerializer.Serialize(customEvent);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try {
                var response = await client.PostAsync(apiUrl, content);
                if(!response.IsSuccessStatusCode) return null;

                var contentType = response.Content.Headers.ContentType?.MediaType;
                if(contentType?.StartsWith("image/") != true) return null;

                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                using var ms = new MemoryStream(imageBytes);
                return Image.Load(ms);
            } catch(Exception) {
                return null;
            }
        }
    }
}
