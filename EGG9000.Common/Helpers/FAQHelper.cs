using Discord;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class FAQHelper {

        public const int MAX_KEYWORD_LENGTH = 20;

        public class FAQBuilder {
            public ComponentBuilder ComponentBuilder { get; set; }
            public EmbedBuilder EmbedBuilder { get; set; }
            public FAQBuilder() { }
        }

        public static async Task<List<FAQTopic>> GetFAQTopicsAsync(this ApplicationDbContext db, Guild guild) {
            if(!db._cache.TryGetValue(guild.GetFAQCacheKey(), out List<FAQTopic> faqTopics)) {
                faqTopics = (await db.Guilds.FirstOrDefaultAsync(g => g.Id == guild.Id)).FAQTopics;
                db._cache.Set(guild.GetFAQCacheKey(), faqTopics, TimeSpan.FromDays(1));
            }
            return faqTopics;
        }

        public static async Task<List<FAQTopic>> QueryFAQTopicsAsync(this ApplicationDbContext db, Guild guild, bool withStaffPerms, string keyword) {
#if DEV9002
            var palaceGuild = await db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == 1108127105088241746);
#else
            var palaceGuild = await db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == 656455567858073601);
#endif
            var faqTopics = (await db.GetFAQTopicsAsync(palaceGuild)).Where(
                f => f.PalaceFAQAppliesToGuild(guild) &&
                (!f.StaffOnly || withStaffPerms)
            ).ToList();
            if(guild.Id != palaceGuild.Id) {
                faqTopics.AddRange(await db.GetFAQTopicsAsync(guild));
            }
            var filteredTopics = faqTopics.Where(f =>
                keyword == "" ||
                (f.Keywords?.Contains(keyword) ?? false) ||
                (f.Keywords?.Any(k => k.ToLowerInvariant().Contains(keyword)) ?? false)
            ).ToList();

            return [.. filteredTopics.OrderByDescending(t => t.Weight)];
        }


        public static string InvalidateFAQTopics(this ApplicationDbContext db, Guild guild) {
            db._cache.Set(guild.GetFAQCacheKey(), new List<FAQTopic>(), TimeSpan.FromMilliseconds(1));
            return guild.GetFAQCacheKey(); 
        }

        private static string GetFAQCacheKey(this Guild g) {
            return "FAQTopicsCache:" + g.Id.ToString();
        }
    }
}
