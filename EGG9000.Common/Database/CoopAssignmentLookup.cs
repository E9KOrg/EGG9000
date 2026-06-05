using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    public sealed record AssignedCoop(Guid CoopId, ulong ThreadId, ulong DiscordChannelId, string Name, string ContractId);

    public readonly record struct CoopAssignmentRow(Guid UserId, Guid CoopId, string ContractId, ulong ThreadId, ulong DiscordChannelId, string Name);

    /// <summary>
    /// Fast user -> assigned-but-not-yet-joined coop lookup, so the "Find my Coop" button
    /// does not hit the DB on every click. Keyed by (DBUser id, contract id). Rebuilt on a
    /// timer (cheap, self-healing) and pruned immediately when a user joins or is removed.
    /// A miss is not authoritative - the caller should fall back to a DB query - so a missed
    /// prune only costs one extra query, never a wrong answer.
    /// </summary>
    public class CoopAssignmentLookup {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILogger<CoopAssignmentLookup> _logger;

        public CoopAssignmentLookup(IDbContextFactory<ApplicationDbContext> dbContextFactory, ILogger<CoopAssignmentLookup> logger) {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        private volatile ConcurrentDictionary<(Guid UserId, string ContractId), List<AssignedCoop>> _map = new();

        public DateTimeOffset? LastRefresh { get; private set; }

        /// <summary>Assigned, not-yet-joined coops for this user+contract, or null on miss (caller falls back to DB).</summary>
        public List<AssignedCoop> Get(Guid userId, string contractId) =>
            _map.TryGetValue((userId, contractId), out var list) ? [.. list] : null;

        public void Remove(Guid userId, string contractId) =>
            _map.TryRemove((userId, contractId), out _);

        /// <summary>Pure grouping used by <see cref="RefreshAsync"/>; deduplicates by coop id and groups by user+contract.</summary>
        public static ConcurrentDictionary<(Guid UserId, string ContractId), List<AssignedCoop>> Build(IEnumerable<CoopAssignmentRow> rows) {
            var map = new ConcurrentDictionary<(Guid, string), List<AssignedCoop>>();
            foreach(var group in rows.GroupBy(r => (r.UserId, r.ContractId))) {
                map[group.Key] = group
                    .GroupBy(r => r.CoopId)
                    .Select(c => c.First())
                    .Select(c => new AssignedCoop(c.CoopId, c.ThreadId, c.DiscordChannelId, c.Name, c.ContractId))
                    .ToList();
            }
            return map;
        }

        public async Task RefreshAsync() {
            try {
                var now = DateTimeOffset.Now;
                await using var db = await _dbContextFactory.CreateDbContextAsync();

                var rows = await db.UserCoopXrefs
                    .IgnoreQueryFilters()
                    .Where(x => !x.JoinedCoop
                             && (int)x.Coop.Status > 2 && (int)x.Coop.Status < 13
                             && x.Coop.CoopEnds > now && !x.Coop.PseudoExpired)
                    .Select(x => new CoopAssignmentRow(x.UserId, x.Coop.Id, x.Coop.ContractID, x.Coop.ThreadID, x.Coop.DiscordChannelId, x.Coop.Name))
                    .ToListAsync();

                _map = Build(rows);
                LastRefresh = DateTimeOffset.Now;
                _logger.LogInformation("Refreshed CoopAssignmentLookup: {Count} user/contract entries", _map.Count);
            } catch(Exception e) {
                _logger.LogError(e, "Error refreshing CoopAssignmentLookup");
            }
        }
    }
}
