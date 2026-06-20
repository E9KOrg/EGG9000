using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class UserGrades(IServiceProvider provider) : _UpdaterBase<UserGrades>(TimeSpan.FromHours(4), TimeSpan.FromMinutes(30), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var users = await _db.DBUsers.Where(x => x.GuildId != 0).ToListAsync(CancellationToken.None);


#if DEBUG
            var throttler = new SemaphoreSlim(8);
            users  = [.. users.Where(x => x.DiscordUsername.StartsWith("heimdallr"))];
#else
            var throttler = new SemaphoreSlim(8);
#endif
            var tasks = new List<Task>();

            Random.Shared.Shuffle(CollectionsMarshal.AsSpan(users));
            foreach(var user in users) {
                await throttler.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () => {
                    foreach(var account in user.EggIncAccounts) {
                        try {
                            using var scope = _provider.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                            if(await AccountRefresh.ApplyExtrasAsync(user, account, db, _logger, CancellationToken.None)) {
                                await db.DBUsers.Where(c => c.Id == user.Id).ExecuteUpdateAsync(s => s
                                    .SetProperty(c => c._contractRegistrationByte, user._contractRegistrationByte));
                            }
                            await db.SaveChangesAsync(CancellationToken.None);
                        } catch(Exception ex) {
                            _logger.LogError(ex, $"Error getting grade for user {user.DiscordUsername} {account.Name}");
                        } finally {
                            await Task.Delay(Random.Shared.Next(200) + 1500);
                            throttler.Release();
                        }
                    }
                }, cancellationToken));
            }
            await Task.WhenAll(tasks);
        }
    }
}
