using Discord;

using EGG9000.Bot;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class RankupMessageHelper {

        public static bool ShouldAnnounce(int newOom, int highWater, bool messagesEnabled, bool groupDisabled) =>
            messagesEnabled && !groupDisabled && newOom > highWater;

        public static List<RankupMessage> SelectPool(IEnumerable<RankupMessage> applicable, int groupBaseOom, bool exclusive) {
            var all = applicable as IList<RankupMessage> ?? applicable.ToList();
            var group = all.Where(m => m.GroupBaseOom == groupBaseOom).ToList();
            var global = all.Where(m => m.GroupBaseOom == RankupMessage.GlobalPool).ToList();
            if(exclusive && group.Count > 0) return group;
            return [.. group, .. global];
        }

        public static RankupMessage WeightedPick(IReadOnlyList<RankupMessage> pool) {
            if(pool is null || pool.Count == 0) return null;
            var total = pool.Sum(m => Math.Max(1, m.Weight));
            var roll = Random.Shared.Next(total);
            foreach(var m in pool) {
                roll -= Math.Max(1, m.Weight);
                if(roll < 0) return m;
            }
            return pool[^1];
        }

        public static async Task<List<RankupMessage>> GetRankupMessagesAsync(this ApplicationDbContext db, Guild guild) {
            if(!db._cache.TryGetValue(GetCacheKey(guild), out List<RankupMessage> messages)) {
                messages = await db.RankupMessages.Where(g => g.GuildId == guild.Id).ToListAsync();
                db._cache.Set(GetCacheKey(guild), messages, TimeSpan.FromMinutes(30));
            }
            return messages ?? [];
        }

        public static async Task<Guild> GetPalaceGuildAsync(this ApplicationDbContext db) {
#if DEV9002
            return await db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == 1108127105088241746);
#else
            return await db.Guilds.AsQueryable().FirstAsync(x => x.DiscordSeverId == 656455567858073601);
#endif
        }

        public static async Task<List<RankupMessage>> QueryApplicableAsync(this ApplicationDbContext db, Guild guild) {
            var palace = await db.GetPalaceGuildAsync();
            var messages = (await db.GetRankupMessagesAsync(palace)).Where(m => m.AppliesToGuild(guild, palace.Id)).ToList();
            if(guild.Id != palace.Id) messages.AddRange(await db.GetRankupMessagesAsync(guild));
            return messages;
        }

        public static async Task<string> PickMessageAsync(this ApplicationDbContext db, DiscordHostedService client, Guild guild, FarmerRank rank, IGuildUser user, double eb) {
            var applicable = await db.QueryApplicableAsync(guild);
            var pool = SelectPool(applicable, rank.GroupBase, guild.RankupExclusivePool);
            var chosen = WeightedPick(pool);
            if(chosen is null) return null;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["user"] = user.Mention,
                ["rank"] = rank.RoleName,
                ["eb"] = eb.ToEggString(),
                ["oom"] = rank.Oom.ToString()
            };
            return await MessageFormatter.FormatAsync(chosen.Text, client, guild.Id, values);
        }

        public static string InvalidateRankupMessages(this ApplicationDbContext db, Guild guild) {
            db._cache.Set(GetCacheKey(guild), new List<RankupMessage>(), TimeSpan.FromMilliseconds(1));
            return GetCacheKey(guild);
        }

        private static string GetCacheKey(Guild g) => "RankupMessagesCache:" + g.Id.ToString();
    }
}
