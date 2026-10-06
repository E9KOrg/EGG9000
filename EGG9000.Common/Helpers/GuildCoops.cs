using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {

    // Backs the API-key co-ops endpoint: the co-ops on one Discord server, for one contract, that
    // hold at least one member of the in-game guild the key is scoped to.
    public static class GuildCoops {

        public static async Task<List<GuildCoopApiItem>> QueryAsync(ApplicationDbContext db, ulong guildId, string contractId, string guildTag, CancellationToken cancellationToken = default) {
            var tag = guildTag?.Trim();
            if(string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(contractId))
                return [];

            // (GuildId, ContractID) is the leading prefix of the existing (GuildId, ContractID, League)
            // index, so this is a seek. The per-player projection is deliberate: loading whole DBUser
            // rows would drag the ship-DM, coop-setting and backup blobs along for every member of
            // every co-op on the contract, and only the two account columns are needed here.
            var rows = await db.Coops
                .AsNoTracking()
                .Where(c => c.GuildId == guildId && c.ContractID == contractId)
                .Select(c => new {
                    c.Name,
                    c.Status,
                    Players = c.UserCoopsXrefs.Select(x => new {
                        x.EggIncId,
                        x.FixedUserName,
                        x.Starter,
                        x.JoinedCoop,
                        x.WasAssigned,
                        x.Removed,
                        x.User.DiscordId,
                        x.User.DiscordUsername,
                        x.User._eggIncIds,
                        x.User._contractRegistrationByte
                    }).ToList()
                })
                .ToListAsync(cancellationToken);

            var coops = new List<GuildCoopApiItem>();
            foreach(var row in rows) {
                var players = new List<GuildCoopPlayerApiItem>(row.Players.Count);
                var guildPlayerCount = 0;

                foreach(var p in row.Players) {
                    var account = ResolveAccount(p._eggIncIds, p._contractRegistrationByte, p.EggIncId);
                    var playerGuild = account?.Guild?.Trim();
                    // Guild tags are typed by hand, so casing and padding never decide membership. Also never trust users to be consistent :(
                    var inGuild = !string.IsNullOrWhiteSpace(playerGuild)
                        && playerGuild.Equals(tag, StringComparison.OrdinalIgnoreCase);

                    // A kicked player is still listed for context, but no longer counts as the guild
                    // member that makes this a guild co-op.
                    if(inGuild && !p.Removed)
                        guildPlayerCount++;

                    players.Add(new GuildCoopPlayerApiItem {
                        DiscordName = p.DiscordUsername,
                        // The key is scoped to one in-game guild, so it only gets the Discord ids of
                        // that guild's members. Everyone else in the co-op is listed without one.
                        DiscordId = inGuild ? p.DiscordId.ToString() : null,
                        EggIncName = p.FixedUserName ?? account?.Name,
                        Guild = playerGuild,
                        InGuild = inGuild,
                        Status = ResolvePlayerStatus(p.Removed, p.JoinedCoop, p.WasAssigned),
                        Starter = p.Starter
                    });
                }

                if(guildPlayerCount == 0)
                    continue;

                coops.Add(new GuildCoopApiItem {
                    CoopCode = row.Name,
                    Status = row.Status.ToString(),
                    GuildPlayerCount = guildPlayerCount,
                    Players = players
                });
            }

            return coops;
        }

        public static string ResolvePlayerStatus(bool removed, bool joinedCoop, bool wasAssigned) {
            if(removed) return "Removed";
            if(joinedCoop) return "Joined";
            if(wasAssigned) return "Assigned";
            return "Pending";
        }

        // A player can have several Egg Inc accounts carrying different guild tags, so membership is
        // decided by the account that actually joined this co-op, and by nothing else. No match means
        // no guild: either the xref names no account, or it names an EID the user has since removed.
        private static EggIncAccount ResolveAccount(string eggIncIds, byte[] contractRegistrationByte, string eggIncId) {
            if(string.IsNullOrWhiteSpace(eggIncId))
                return null;

            var accounts = DBUser.FromAccountColumns(eggIncIds, contractRegistrationByte).EggIncAccounts;
            if(accounts is null || accounts.Count == 0)
                return null;

            return accounts.FirstOrDefault(a => string.Equals(a.Id, eggIncId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class GuildCoopApiItem {
        public string CoopCode { get; set; }
        public string Status { get; set; }
        public int GuildPlayerCount { get; set; }
        public List<GuildCoopPlayerApiItem> Players { get; set; }
    }

    
    public class GuildCoopPlayerApiItem {
        public string DiscordName { get; set; }
        public string DiscordId { get; set; }
        public string EggIncName { get; set; }
        public string Guild { get; set; }
        public bool InGuild { get; set; }
        public string Status { get; set; }
        public bool Starter { get; set; }
    }
}
