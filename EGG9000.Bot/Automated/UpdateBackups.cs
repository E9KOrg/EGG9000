using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class UpdateBackups(IServiceProvider provider) : _UpdaterBase<UpdateBackups>(TimeSpan.FromMinutes(1), delayedStart: TimeSpan.FromMinutes(0), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {

            var times = new TimingsFactory(_logger).Start();

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var longestBackupAgo = await _db.DBUsers.Where(x => x.GuildId > 0 && !x.TempDisabled && x.LastBackupCheck != null).OrderBy(x => x.LastBackupCheck).Select(x => x.LastBackupCheck).FirstOrDefaultAsync() ?? DateTimeOffset.UtcNow;
            _logger.LogInformation($"Longest backup check ago: {(DateTimeOffset.UtcNow - longestBackupAgo).TotalMinutes} minutes");


            var usersToCheck = await _db.DBUsers.Where(x => x.GuildId > 0 && !x.TempDisabled).OrderBy(x => x.LastBackupCheck).Take(10).ToListAsync();

            var guilds = await _db.Guilds.ToListAsync();

            times.Set("Fetched DB Users");

            foreach(var user in usersToCheck) {
                var update = false;
                foreach(var account in user.EggIncAccounts) {
                    var backup = await ContractsAPI.GetBackupAsync(account.Id);

                    _logger.LogTrace($"Getting backups for {user.DiscordUsername} {account.Name ?? account.Id}");
                    if(backup?.Farms is not null) {
                        account.Backup = backup;

                        var guild = _client.Guilds.FirstOrDefault(x => x.Id == user.GuildId);
                        var dbGuild = guilds.FirstOrDefault(x => x.Id == user.GuildId);
                        await SubscriptionUpdater.UpdateSubscriptionForAccount(_client, guild, dbGuild, user, account, _logger);
                        update = true;

                        // TODO: Track current game version and notify if newer version is available
                    }
                }
                if(update) {
                    user.UpdateAccounts();
                }
                user.LastBackupCheck = DateTimeOffset.UtcNow;
            }

            times.Set("Updated Backups");
            await _db.SaveChangesAsync(cancellationToken);

            await ShipReturnDM.UpdateNextShipDM(usersToCheck, _db);
            var finished = times.Finished();
            _logger.LogInformation($"Updated {usersToCheck.Count} user backups. in {finished.Last().time.TotalMilliseconds} ms");
        }
    }
}
