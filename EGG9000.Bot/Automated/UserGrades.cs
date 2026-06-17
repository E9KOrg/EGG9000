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
                            var (info, error) = await EggIncApi.GetContractPlayerInfo(account.Id);
                            if(info == null) {
                                _logger.LogWarning($"No response getting grade for user {user.DiscordUsername} {account.Name}: {error}");
                            } else if(info.Status != Ei.ContractPlayerInfo.Types.Status.Complete) {
                                _logger.LogTrace($"Skipping non-final grade ({info.Status}) for user {user.DiscordUsername} {account.Name}");
                            } else {
                                using var scope = _provider.CreateScope();
                                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                                if(GradeSync.ApplyGradeChange(user, account, info.Grade, setPromotionTime: false, guardUnset: true, _logger)) {
                                    await db.DBUsers.Where(c => c.Id == user.Id).ExecuteUpdateAsync(s => s
                                        .SetProperty(c => c._contractRegistrationByte, user._contractRegistrationByte));
                                }

                                if(info.SeasonProgress.Count > 0) {
                                    var seasonIds = info.SeasonProgress
                                        .Select(sp => sp.SeasonId)
                                        .Where(id => !string.IsNullOrEmpty(id))
                                        .ToList();
                                    var existingRows = await db.UserSeasonProgresses
                                        .Where(x => x.EggIncId == account.Id && seasonIds.Contains(x.SeasonId))
                                        .ToListAsync(CancellationToken.None);

                                    foreach(var sp in info.SeasonProgress) {
                                        if(string.IsNullOrEmpty(sp.SeasonId)) continue;
                                        var row = existingRows.FirstOrDefault(x => x.SeasonId == sp.SeasonId);
                                        if(row == null) {
                                            db.UserSeasonProgresses.Add(new UserSeasonProgress {
                                                EggIncId = account.Id,
                                                SeasonId = sp.SeasonId,
                                                TotalCxp = sp.TotalCxp,
                                                StartingGrade = (int)sp.StartingGrade
                                            });
                                        } else {
                                            row.TotalCxp = sp.TotalCxp;
                                            row.StartingGrade = (int)sp.StartingGrade;
                                        }
                                    }
                                    await db.SaveChangesAsync(CancellationToken.None);
                                }
                            }
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
