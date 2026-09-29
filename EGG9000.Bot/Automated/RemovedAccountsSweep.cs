using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class RemovedAccountsSweep(IServiceProvider provider) : _UpdaterBase<RemovedAccountsSweep>(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(2), provider) {
        public sealed record Reapplied(Guid UserId, ulong DiscordId, ulong GuildId, string EggIncId, int Count);
        public sealed record Owner(Guid UserId, ulong DiscordId);

        private readonly HashSet<string> _loggedDuplicates = [];

        public async override Task Run(object state, CancellationToken cancellationToken) {
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await ReapplyRemovalsAsync(db, _logger, cancellationToken);

            foreach(var (eggIncId, owners) in await FindDuplicateOwnersAsync(db, cancellationToken)) {
                if(!_loggedDuplicates.Add($"{eggIncId}|{string.Join(",", owners.Select(o => o.UserId).Order())}")) continue;
                _logger.LogWarning("EggInc ID {EggIncId} is registered to {Count} users: {Users}", eggIncId, owners.Count, string.Join(", ", owners.Select(o => o.DiscordId)));
            }
        }

        public static async Task<List<Reapplied>> ReapplyRemovalsAsync(ApplicationDbContext db, ILogger logger, CancellationToken cancellationToken) {
            var tombstones = await db.RemovedAccounts.ToListAsync(cancellationToken);
            if(tombstones.Count == 0) return [];

            var userIds = tombstones.Select(t => t.UserId).Distinct().ToList();
            var users = await db.DBUsers.Where(u => userIds.Contains(u.Id)).ToListAsync(cancellationToken);
            var hits = new List<Reapplied>();
            foreach(var user in users) {
                var accounts = user.EggIncAccounts;
                if(user.AccountsUnreadable) continue;
                foreach(var tombstone in tombstones.Where(t => t.UserId == user.Id)) {
                    if(accounts.RemoveAll(a => string.Equals(a.Id, tombstone.EggIncId, StringComparison.OrdinalIgnoreCase)) == 0) continue;
                    user.UpdateAccounts();
                    tombstone.ReappliedCount++;
                    tombstone.LastReappliedOn = DateTimeOffset.UtcNow;
                    logger.LogWarning("Removed EggInc ID {EggIncId} reappeared on user {DiscordId} ({UserId}) and was removed again, {Count} time(s) so far", tombstone.EggIncId, user.DiscordId, user.Id, tombstone.ReappliedCount);
                    hits.Add(new(user.Id, user.DiscordId, user.GuildId, tombstone.EggIncId, tombstone.ReappliedCount));
                }
            }
            if(hits.Count > 0) await db.SaveChangesAsync(cancellationToken);
            return hits;
        }

        public static async Task<Dictionary<string, List<Owner>>> FindDuplicateOwnersAsync(ApplicationDbContext db, CancellationToken cancellationToken) {
            var rows = await db.DBUsers
                .Select(u => new { u.Id, u.DiscordId, u._eggIncIds, u._contractRegistrationByte })
                .ToListAsync(cancellationToken);
            var owners = new Dictionary<string, List<Owner>>(StringComparer.OrdinalIgnoreCase);
            foreach(var row in rows) {
                var projected = DBUser.FromAccountColumns(row._eggIncIds, row._contractRegistrationByte);
                projected.Id = row.Id;
                projected.DiscordId = row.DiscordId;
                var ids = projected.EggIncAccounts.Select(a => a.Id).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach(var id in ids) {
                    if(!owners.TryGetValue(id, out var list)) owners[id] = list = [];
                    list.Add(new(row.Id, row.DiscordId));
                }
            }
            return owners.Where(kv => kv.Value.Count > 1).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }
    }
}
