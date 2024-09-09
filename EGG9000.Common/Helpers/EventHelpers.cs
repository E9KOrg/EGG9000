using EGG9000.Common.Database.Entities;
using EGG9000.Common.Database;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace EGG9000.Common.Helpers {
    public static class EventHelpers {

        public static async Task<List<EventCustomization>> GetCustomizationsAsync(this ApplicationDbContext db, Guild dbGuild) {
            if(!db._cache.TryGetValue(dbGuild.GetECCacheKey(), out List<EventCustomization> customizations)) {
                customizations = (await db.Guilds.AsQueryable().FirstOrDefaultAsync(g => g.Id == dbGuild.Id)).EventCustomizations;
                db._cache.Set(dbGuild.GetECCacheKey(), customizations, TimeSpan.FromDays(1));
            }
            return customizations;
        }

        public static async Task<EventCustomization> GetCustomizationAsync(this ApplicationDbContext db, Guild dbguild, Event customEvent) {
            return await db.GetCustomizationAsync(dbguild, customEvent.Type);
        }

        public static async Task<EventCustomization> GetCustomizationAsync(this ApplicationDbContext db, Guild dbguild, string eventType) {
#if DEV9002
            var palaceGuild = await db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == 1108127105088241746);
#else
            var palaceGuild = await db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == 656455567858073601);
#endif
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

        public static string GetEventImagePath(Event customEvent) {
#if RELEASE
            var baseDir = "c:/Websites/EGG9000";
#else
            var baseDir = Directory.GetParent(Environment.CurrentDirectory).Parent.Parent.FullName.Replace("\\", "/") + "/../EGG9000.Site";
#endif

            var eventImageDir = Path.Combine(baseDir, "wwwroot/images/events", $"event_{customEvent.Type.ToLowerInvariant().Replace("-", "_")}.png");
            return File.Exists(eventImageDir) ? eventImageDir : null;
        }

        public static Image GetEventImage(this ApplicationDbContext db, Event customEvent) {
            var eventKey = $"{customEvent.Type.ToLowerInvariant()}-U:{customEvent.CcOnly}";
            if(!db._cache.TryGetValue(eventKey, out Image image)) {
                image = GenerateEventImage(customEvent);
                db._cache.Set(eventKey, image);
            }
            return image;
        }

        private static Image GenerateEventImage(Event customEvent) {
            var baseImage = Image.Load(GetEventImagePath(customEvent));
            var backgroundColor = customEvent.Type.ToLower() switch {
                "epic-research-sale" => "#ef4444",
                "piggy-boost" => "#f97316",
                "piggy-cap-boost" => "#f59e0b",
                "prestige-boost" => "#f59e0b",
                "earnings-boost" => "#84cc16",
                "gift-boost" => "#10b981",
                "drone-boost" => "#10b981",
                "research-sale" => "#14b8a6",
                "hab-sale" => "#06b6d4",
                "vehicle-sale" => "#0ea5e9",
                "boost-sale" => "#3b82f6",
                "boost-duration" => "#6366f1",
                "crafting-sale" => "#8b5cf6",
                "mission-fuel" => "#8b5cf6",
                "mission-capacity" => "#d946ef",
                "mission-duration" => "#ec4899",
                "shell-sale" => "#f43f5e",
                _ => "#9ca3af"
            };

            // Convert hex color to a Color object
            var bgColor = Color.ParseHex(backgroundColor);

            // Calculate new dimensions (110% of original for padding)
            var newWidth = (int)(baseImage.Width * 1.1);
            var newHeight = (int)(baseImage.Height * 1.1);

            // Create a new image with the new dimensions and the background color
            var newImage = new Image<Rgba32>(newWidth, newHeight);

            // If the event is ULTRA-only, use a color gradient from #f5a709 (left) to 900fb1 (right)
            if(customEvent.CcOnly) {
                // Define the gradient colors
                var leftColor = Color.ParseHex("#f5a709");
                var rightColor = Color.ParseHex("#900fb1");

                // Create a linear gradient brush from left to right
                var gradientBrush = new LinearGradientBrush(
                    new PointF(0, 0),             // Start point (left side)
                    new PointF(newWidth, 0),      // End point (right side)
                    GradientRepetitionMode.None,  // No repetition of gradient
                    new ColorStop(0, leftColor),  // Start with leftColor at 0%
                    new ColorStop(1, rightColor)  // End with rightColor at 100%
                );

                // Apply the gradient fill to the new image
                newImage.Mutate(ctx => ctx.Fill(gradientBrush));
            } else {
                newImage.Mutate(ctx => ctx.Fill(bgColor));
            }

            // Center the original image onto the new canvas
            var xPos = (newWidth - baseImage.Width) / 2;
            var yPos = (newHeight - baseImage.Height) / 2;
            newImage.Mutate(ctx => ctx.DrawImage(baseImage, new Point(xPos, yPos), 1f));

            return newImage;
        }
    }
}
