using EGG9000.Common.Database.Entities;
using EGG9000.Common.Database;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

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

        public static void InvalidateEventCustomizations(this ApplicationDbContext db, Guild guild) {
            db._cache.Set(guild.GetECCacheKey(), new List<EventCustomization>(), TimeSpan.FromMilliseconds(1));
        }

        private static string GetECCacheKey(this Guild g) {
            return "EventCustomizationCache:" + g.Id.ToString();
        }
    }
}
