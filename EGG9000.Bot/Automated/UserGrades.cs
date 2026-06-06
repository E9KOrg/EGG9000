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
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class UserGrades(IServiceProvider provider) : _UpdaterBase<UserGrades>(TimeSpan.FromHours(4), TimeSpan.FromMinutes(30), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var users = await _db.DBUsers.Where(x => x.GuildId != 0).ToListAsync(CancellationToken.None);


            var throttler = new SemaphoreSlim(8);

            var tasks = new List<Task>();

            foreach(var user in users) {
                await throttler.WaitAsync(cancellationToken);
                tasks.Add(Task.Run(async () => {
                    foreach(var account in user.EggIncAccounts) {
                        try {
                            var response = await EggIncApi.GetContractPlayerInfo(account.Id);
                            if(response == null) {
                                _logger.LogWarning($"No response getting grade for user {user.DiscordUsername} {account.Name}");
                            } else if(GradeSync.ApplyGradeChange(user, account, response.Grade, setPromotionTime: false, guardUnset: true, _logger)) {
                                using var writeScope = _provider.CreateScope();
                                var writeDb = writeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                                await writeDb.DBUsers.Where(c => c.Id == user.Id).ExecuteUpdateAsync(s => s
                                    .SetProperty(c => c._contractRegistrationByte, user._contractRegistrationByte));
                                writeDb.Dispose();
                            }
                        } catch(Exception ex) {
                            _logger.LogError(ex, $"Error getting grade for user {user.DiscordUsername} {account.Name}");
                        } finally {
                            throttler.Release();
                        }
                    }
                }, cancellationToken));
            }
            await Task.WhenAll(tasks);
        }
    }
}
