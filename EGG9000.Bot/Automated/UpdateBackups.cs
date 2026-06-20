using Discord.WebSocket;

using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Nito.AsyncEx;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Frozen;

namespace EGG9000.Bot.Automated {
    public class UpdateBackups(IServiceProvider provider) : _UpdaterBase<UpdateBackups>(TimeSpan.FromMinutes(1), delayedStart: TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {

            var times = new TimingsFactory(_logger).Start();

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cachedContracts = await _db.CachedEiContractsAsync();

            var guilds = await _db.Guilds.ToListAsync();
            var guildIDs = guilds.Select(x => x.Id).ToHashSet();

            var usersToCheck = await _db.DBUsers.Where(x => guildIDs.Contains(x.GuildId) && x.GuildId > 0 && !x.TempDisabled).OrderBy(x => x.LastBackupCheck).Take(75).ToListAsync();
            var longestBackupAgo = usersToCheck.Where(x => x.LastBackupCheck != null).OrderBy(x => x.LastBackupCheck).Select(x => x.LastBackupCheck).FirstOrDefault() ?? DateTimeOffset.UtcNow;
            _logger.LogInformation($"Longest backup check ago: {(DateTimeOffset.UtcNow - longestBackupAgo).Humanize(precision: 2)}");


            times.Set("Fetched DB Users");


            var throttler = new SemaphoreSlim(8);

            var tasks = new List<Task>();
            var discoveredContractDefs = new System.Collections.Concurrent.ConcurrentDictionary<string, Ei.Contract>();
            var knownContractIds = cachedContracts.Select(c => c.Identifier).ToHashSet();

            foreach(var user in usersToCheck) {
                await throttler.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () => {
                    _logger.LogTrace($"Updating {user.DiscordUsername}");
                    try {
                        await UpdateUser(user, guilds, cachedContracts, knownContractIds, discoveredContractDefs);
                    } catch(Exception ex) {
                        _logger.LogError(ex, $"Error updating user {user.DiscordUsername} {user.Id}");
                    } finally {
                        throttler.Release();
                    }
                }, cancellationToken));
            }
            await Task.WhenAll(tasks);
            times.Set("Updated Backups");
            await _db.SaveChangesAsync(cancellationToken);

            var registered = await _db.RegisterMissingContractsAsync(discoveredContractDefs.Values, cancellationToken);
            if(registered > 0)
                _logger.LogInformation("Self-healed {count} contract(s) missing from the DB from player backups", registered);

            await ShipReturnDM.UpdateNextShipDM(usersToCheck, _db);
            var finished = times.Finished();
            _logger.LogInformation($"Updated {usersToCheck.Count} user backups. in {finished.Last().time.Humanize(precision: 2)}");
        }

        // Contract identifiers the server has explicitly reported as not-found, so we stop re-fetching
        // them every cycle. Lives for the process lifetime; only populated from authoritative not_found
        // responses (never from transient network failures).
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _notFoundContractIds = new();

        // Some contracts (e.g. single-player ones) are never delivered to our reference account through
        // get_periodicals, so NewContracts never absorbs them. For every contract a player references
        // (active or archived) that we have no definition for, fetch it by identifier and stage it for
        // registration. This is the same id set CustomBackup.AddContracts would otherwise skip.
        private static async Task DiscoverUnknownContracts(string eggIncId, Ei.Backup backup, HashSet<string> knownContractIds, System.Collections.Concurrent.ConcurrentDictionary<string, Ei.Contract> discoveredContractDefs) {
            if(backup?.Contracts is null)
                return;

            var missing = backup.Contracts.Contracts.Concat(backup.Contracts.Archive)
                .Where(x => x is not null)
                .Select(x => x.ContractIdentifier)
                .Where(id => !string.IsNullOrEmpty(id)
                    && !knownContractIds.Contains(id)
                    && !discoveredContractDefs.ContainsKey(id)
                    && !_notFoundContractIds.ContainsKey(id))
                .Distinct()
                .ToArray();

            if(missing.Length == 0)
                return;

            foreach(var chunk in missing.Chunk(50)) {
                var (info, _) = await EggIncApi.GetContractsInfoAsync(eggIncId, chunk);
                if(info is null)
                    continue;
                foreach(var def in info.Contracts) {
                    if(!string.IsNullOrEmpty(def.Identifier))
                        discoveredContractDefs.TryAdd(def.Identifier, def);
                }
                foreach(var notFound in info.NotFound)
                    _notFoundContractIds.TryAdd(notFound, 0);
            }
        }

        public async Task UpdateUser(DBUser user, List<Guild> guilds, FrozenSet<Ei.Contract> cachedContracts, HashSet<string> knownContractIds, System.Collections.Concurrent.ConcurrentDictionary<string, Ei.Contract> discoveredContractDefs) {
            var update = false;
            foreach(var account in user.EggIncAccounts) {
                var firstContact = await EggIncApi.FirstContact(account.Id);
                var dbGuild = guilds.FirstOrDefault(x => x.Id == user.GuildId);

                if(dbGuild is null)
                    continue;

                await DiscoverUnknownContracts(account.Id, firstContact?.Backup, knownContractIds, discoveredContractDefs);

                var oldLevel = account.SubscriptionLevel;
                var backup = new CustomBackup(firstContact.Backup, cachedContracts, account.Backup);

                _logger.LogTrace($"Getting backups for {user.DiscordUsername} {account.Name ?? account.Id}");
                if(backup?.Farms is not null) {

                    // Backup setter auto-syncs account.SubscriptionLevel and account.SubscriptionEnds
                    account.Backup = backup;

                    if(firstContact.Backup.SubInfo is null) {
                        _logger.LogWarning($"No subscription info in backup for {user.DiscordUsername} {account.Id}, fetching from API. Last backup: {account.Backup?.GetLastBackupDateTime()}");
                        var (subscription, subError) = await EggIncApi.GetUserSubscription(backup.EggIncId);
                        if(subscription is null) {
                            _logger.LogWarning($"Failed to fetch subscription for {user.DiscordUsername} {account.Id}: {subError}");
                        } else {
                            account.SubscriptionLevel = subscription.SubscriptionLevel;
                            account.SubscriptionEnds = subscription.PeriodEnd;
                            account.Backup.SubscriptionLevel = subscription.SubscriptionLevel;
                            account.Backup.SubscriptionEnds = subscription.PeriodEnd;
                        }
                    }

                    // Always enforce roles - CheckRole is idempotent and only calls Discord API when hasRole != needsRole
                    var guild = _client.Guilds.FirstOrDefault(x => x.Id == user.GuildId);
                    await SubscriptionHelper.SubscriptionLevelChanged(_client.Gateway, guild, dbGuild, user, account, _logger, oldLevel);

                    update = true;

                    // TODO: Track current game version and notify if newer version is available
                } else {
                    _logger.LogWarning($"Failed to get backup for {user.DiscordUsername} {account.Name ?? account.Id}");
                }
                if(update) {
                    user.UpdateAccounts();
                }

            }
            user.LastBackupCheck = DateTimeOffset.UtcNow;
        }
    }
}
