using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Site.Services {

    // The lb is built for both the HomeController views and the API-key JSON endpoint on
    // APIController, so it lives here instead of on either controller.
    public class LeaderboardService(ApplicationDbContext db, DiscordSocketClient discord, DatabaseCache databaseCache) {

        private readonly ApplicationDbContext _db = db;
        private readonly DiscordSocketClient _discord = discord;
        private readonly DatabaseCache _databaseCache = databaseCache;

        public async Task<List<LeaderboardUser>> GetLeaderboardAsync(ulong guildid) {
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guildid);

            var guild = _discord.Guilds.FirstOrDefault(g => g.Id == guildid);
            if(guild is null) return [];
            // Membership is the DB GuildId, not the live Discord cache. The site runs its own bare
            // socket client whose member cache can read "complete" while actually partial, and gating
            // on it dropped real members from the board.
            // ManageOverflow owns reconciling GuildId against true Discord membership; the board just
            // trusts it. DiscordUser is still resolved below for the display name only.
            var allUsers = await _databaseCache.GetDbUsers();
            var rawusers = allUsers.Where(x => x.GuildId == guildid && !x.TempDisabled);

            var accounts = rawusers.SelectMany(dbu => dbu.EggIncAccounts.Select(y => new LeaderboardUser {
                User = dbu,
                Backup = y.Backup,
                DiscordUser = guild.Users.FirstOrDefault(du => du.Id == dbu.DiscordId),
                TotalContracts = dbu.GuildCoops,
                TotalCS = y.Backup?.TotalCS ?? 0,
                SeasonCS = y.Backup?.SeasonCS ?? 0,
                TotalCraftingXP = y.Backup?.CraftingXP ?? 0,
                CraftingLevel = y.Backup?.GetCraftingLevel() ?? 1,
            })).Where(x => x.Backup != null && x.Backup.Farms.Count > 0 && (x.Account.Active || guildid == 1108127105088241746)).OrderByDescending(x => x.Backup.EarningsBonus).ToList();

            return accounts;
        }
    }
}
