using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    public record ContractStats(string ContractId, string ContractName, int ActiveCoops,
        int PendingThreads, int FinishedCoops, int UsersAssigned, double AverageFill);

    public record ServerStats(int ActiveContracts, int ActiveCoops, int PendingThreads,
        int FinishedCoops, int UsersAssigned);

    /// <summary>
    /// Periodically refreshed, in memory snapshot of co-op stats per server and per
    /// contract. Computed from the database only (no Discord calls) so it stays cheap.
    /// Refreshed by CoopStatsRefreshService on a timer; reads are lock free against an
    /// atomically swapped snapshot.
    /// </summary>
    public class CoopStatsCache {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ILogger<CoopStatsCache> _logger;

        public CoopStatsCache(IDbContextFactory<ApplicationDbContext> dbContextFactory, ILogger<CoopStatsCache> logger) {
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        private volatile IReadOnlyDictionary<(ulong GuildId, string ContractId), ContractStats> _contractStats
            = new Dictionary<(ulong, string), ContractStats>();
        private volatile IReadOnlyDictionary<ulong, ServerStats> _serverStats
            = new Dictionary<ulong, ServerStats>();

        public DateTimeOffset? LastRefresh { get; private set; }

        public ContractStats GetContractStats(ulong guildId, string contractId) =>
            _contractStats.TryGetValue((guildId, contractId), out var s) ? s : null;

        public ServerStats GetServerStats(ulong guildId) =>
            _serverStats.TryGetValue(guildId, out var s) ? s : null;

        private sealed class CoopRow {
            public ulong GuildId { get; init; }
            public string ContractID { get; init; }
            public Guid CoopId { get; init; }
            public CoopStatusEnum Status { get; init; }
            public ulong ThreadID { get; init; }
            public int? CurrentUsers { get; init; }
            public int? MaxUsers { get; init; }
            public DateTimeOffset? CoopEnds { get; init; }
        }

        public async Task RefreshAsync() {
            try {
                var now = DateTimeOffset.Now;
                await using var db = await _dbContextFactory.CreateDbContextAsync();

                var guildContracts = await db.GuildContracts.Include(g => g.Contract)
                    .Where(g => !g.DeletedChannel && g.Contract.GoodUntil > now)
                    .ToListAsync();

                var contractIds = guildContracts.Select(g => g.ContractID).Distinct().ToList();

                var coops = await db.Coops
                    .Where(c => contractIds.Contains(c.ContractID) && c.Created > now.AddDays(-30))
                    .Select(c => new CoopRow {
                        GuildId = c.GuildId,
                        ContractID = c.ContractID,
                        CoopId = c.Id,
                        Status = c.Status,
                        ThreadID = c.ThreadID,
                        CurrentUsers = c.CurrentUsers,
                        MaxUsers = c.MaxUsers,
                        CoopEnds = c.CoopEnds
                    })
                    .ToListAsync();

                var coopIds = coops.Select(c => c.CoopId).ToList();
                var distinctXrefs = await db.UserCoopXrefs
                    .Where(x => coopIds.Contains(x.CoopId))
                    .Select(x => new { x.CoopId, x.UserId })
                    .Distinct()
                    .ToListAsync();
                var usersByCoop = distinctXrefs
                    .GroupBy(x => x.CoopId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToHashSet());

                static bool IsActive(CoopRow c, DateTimeOffset n) =>
                    (int)c.Status > 2 && (int)c.Status < 13 && c.CoopEnds > n;
                static bool IsPending(CoopRow c) =>
                    c.Status == CoopStatusEnum.WaitingOnThread && c.ThreadID == 0;
                static bool IsFinished(CoopRow c) =>
                    c.Status == CoopStatusEnum.Completed || c.Status == CoopStatusEnum.Failed
                    || c.Status == CoopStatusEnum.CompletedAllCheckIn;

                var contractStats = new Dictionary<(ulong, string), ContractStats>();

                // Map a coop's contractId to the friendly name (first matching GuildContract).
                var nameByContract = guildContracts
                    .GroupBy(g => g.ContractID)
                    .ToDictionary(g => g.Key, g => g.First().Contract?.Name ?? g.Key);

                foreach(var group in coops.GroupBy(c => (c.GuildId, c.ContractID))) {
                    var active = group.Where(c => IsActive(c, now)).ToList();
                    var usersAssigned = group
                        .SelectMany(c => usersByCoop.TryGetValue(c.CoopId, out var u) ? u : new HashSet<Guid>())
                        .Distinct().Count();
                    var fills = active
                        .Where(c => c.MaxUsers is > 0)
                        .Select(c => (double)(c.CurrentUsers ?? 0) / c.MaxUsers.Value)
                        .ToList();

                    contractStats[group.Key] = new ContractStats(
                        ContractId: group.Key.ContractID,
                        ContractName: nameByContract.TryGetValue(group.Key.ContractID, out var nm) ? nm : group.Key.ContractID,
                        ActiveCoops: active.Count,
                        PendingThreads: group.Count(IsPending),
                        FinishedCoops: group.Count(IsFinished),
                        UsersAssigned: usersAssigned,
                        AverageFill: fills.Count > 0 ? fills.Average() : 0);
                }

                var serverStats = new Dictionary<ulong, ServerStats>();
                foreach(var byGuild in coops.GroupBy(c => c.GuildId)) {
                    var active = byGuild.Where(c => IsActive(c, now)).ToList();
                    var usersAssigned = byGuild
                        .SelectMany(c => usersByCoop.TryGetValue(c.CoopId, out var u) ? u : new HashSet<Guid>())
                        .Distinct().Count();
                    var activeContracts = byGuild
                        .Where(c => IsActive(c, now))
                        .Select(c => c.ContractID)
                        .Distinct().Count();

                    serverStats[byGuild.Key] = new ServerStats(
                        ActiveContracts: activeContracts,
                        ActiveCoops: active.Count,
                        PendingThreads: byGuild.Count(IsPending),
                        FinishedCoops: byGuild.Count(IsFinished),
                        UsersAssigned: usersAssigned);
                }

                _contractStats = contractStats;
                _serverStats = serverStats;
                LastRefresh = DateTimeOffset.Now;
                _logger.LogInformation("Refreshed CoopStatsCache: {Contracts} contract entries across {Guilds} guilds",
                    contractStats.Count, serverStats.Count);
            } catch(Exception e) {
                _logger.LogError(e, "Error refreshing CoopStatsCache");
            }
        }
    }
}
