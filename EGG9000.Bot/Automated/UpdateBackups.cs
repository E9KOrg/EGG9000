using Discord.WebSocket;

using EGG9000.Bot.EggIncAPI;
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

namespace EGG9000.Bot.Automated {
    public class UpdateBackups(IServiceProvider provider) : _UpdaterBase<UpdateBackups>(TimeSpan.FromMinutes(1), delayedStart: TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {

            var times = new TimingsFactory(_logger).Start();

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var guilds = await _db.Guilds.ToListAsync();
            var guildIDs = guilds.Select(x => x.Id).ToHashSet();

            var usersToCheck = await _db.DBUsers.Where(x => guildIDs.Contains(x.GuildId) && x.GuildId > 0 && !x.TempDisabled).OrderBy(x => x.LastBackupCheck).Take(45).ToListAsync();
            var longestBackupAgo = usersToCheck.Where(x => x.LastBackupCheck != null).OrderBy(x => x.LastBackupCheck).Select(x => x.LastBackupCheck).FirstOrDefault() ?? DateTimeOffset.UtcNow;
            _logger.LogInformation($"Longest backup check ago: {(DateTimeOffset.UtcNow - longestBackupAgo).Humanize(precision: 2)}");


            times.Set("Fetched DB Users");


            var throttler = new SemaphoreSlim(6);

            var tasks = new List<Task>();

            foreach(var user in usersToCheck) {
                await throttler.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () => {
                    _logger.LogTrace($"Updating {user.DiscordUsername}");
                    try {
                        await UpdateUser(user, guilds);
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

            await ShipReturnDM.UpdateNextShipDM(usersToCheck, _db);
            var finished = times.Finished();
            _logger.LogInformation($"Updated {usersToCheck.Count} user backups. in {finished.Last().time.Humanize(precision: 2)}");
        }

        public async Task UpdateUser(DBUser user, List<Guild> guilds) {
            var update = false;
            foreach(var account in user.EggIncAccounts) {
                var firstContact = await ContractsAPI.FirstContact(account.Id);
                var dbGuild = guilds.FirstOrDefault(x => x.Id == user.GuildId);

                if(dbGuild is null)
                    continue;
                var oldLevel = account.SubscriptionLevel;
                var backup = new CustomBackup(firstContact.Backup);

                _logger.LogTrace($"Getting backups for {user.DiscordUsername} {account.Name ?? account.Id}");
                if(backup?.Farms is not null) {

                    // Backup setter auto-syncs account.SubscriptionLevel and account.SubscriptionEnds
                    account.Backup = backup;

                    if(firstContact.Backup.SubInfo is null) {
                        _logger.LogWarning($"No subscription info in backup for {user.DiscordUsername} {account.Id}, fetching from API");
                        var subscription = await ContractsAPI.GetUserSubscription(backup.EggIncId);
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
