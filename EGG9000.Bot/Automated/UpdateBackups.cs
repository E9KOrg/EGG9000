using Discord.WebSocket;

using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Common.Migrations;

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

            foreach(var user in usersToCheck) {
                await throttler.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () => {
                    _logger.LogTrace($"Updating {user.DiscordUsername}");
                    try {
                        await UpdateUser(user, guilds, cachedContracts, discoveredContractDefs);
                    } catch(Exception ex) {
                        _logger.LogError(ex, $"Error updating user {user.DiscordUsername} {user.Id}");
                    } finally {
                        throttler.Release();
                    }
                }, cancellationToken));
            }
            await Task.WhenAll(tasks);
            Console.WriteLine(String.Join(",", tasks.Select(x => x.IsCompleted ? "X" : "_")));
            times.Set("Updated Backups");
            await _db.SaveChangesAsync(cancellationToken);

            var registered = await _db.RegisterMissingContractsAsync(discoveredContractDefs.Values, cancellationToken);
            if(registered > 0)
                _logger.LogInformation("Self-healed {count} contract(s) missing from the DB from player backups", registered);

            await ShipReturnDM.UpdateNextShipDM(usersToCheck, _db);
            var finished = times.Finished();
            _logger.LogInformation($"Updated {usersToCheck.Count} user backups. in {finished.Last().time.Humanize(precision: 2)}");
        }

        public async Task UpdateUser(DBUser user, List<Guild> guilds, FrozenSet<Ei.Contract> cachedContracts, System.Collections.Concurrent.ConcurrentDictionary<string, Ei.Contract> discoveredContractDefs) {
            var update = false;
            foreach(var account in user.EggIncAccounts) {
                var firstContact = await EggIncApi.FirstContact(account.Id);
                var dbGuild = guilds.FirstOrDefault(x => x.Id == user.GuildId);

                if(dbGuild is null)
                    continue;

                if(firstContact?.Backup?.Contracts is not null) {
                    foreach(var lc in firstContact.Backup.Contracts.Contracts.Concat(firstContact.Backup.Contracts.Archive)) {
                        if(lc.Contract is not null && !string.IsNullOrEmpty(lc.Contract.Identifier))
                            discoveredContractDefs.TryAdd(lc.Contract.Identifier, lc.Contract);
                    }
                }

                var oldLevel = account.SubscriptionLevel;
                var backup = new CustomBackup(firstContact.Backup, cachedContracts);

                _logger.LogTrace($"Getting backups for {user.DiscordUsername} {account.Name ?? account.Id}");
                if(backup?.Farms is not null) {

                    // Backup setter auto-syncs account.SubscriptionLevel and account.SubscriptionEnds
                    account.Backup = backup;

                    if(firstContact.Backup.SubInfo is null) {
                        _logger.LogWarning($"No subscription info in backup for {user.DiscordUsername} {account.Id}, fetching from API");
                        var subscription = await EggIncApi.GetUserSubscription(backup.EggIncId);
                        account.SubscriptionLevel = subscription.SubscriptionLevel;
                        account.SubscriptionEnds = subscription.PeriodEnd;
                        account.Backup.SubscriptionLevel = subscription.SubscriptionLevel;
                        account.Backup.SubscriptionEnds = subscription.PeriodEnd;
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
