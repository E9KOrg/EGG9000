using Cronos;
using EGG9000.Common.Database;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using Ei;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class HandleGradeChanges(IServiceProvider provider) : _UpdaterBase<HandleGradeChanges>(CronExpression.Parse("30 10,18,23 * * 1,3,5"), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            List<DBUser> users;
            using(var lookupScope = _provider.CreateScope()) {
                var lookupDb = lookupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                users = await lookupDb.DBUsers.AsNoTracking().Where(x => x.GuildId == 656455567858073601).ToListAsync(CancellationToken.None);
            }

            var chunkedUsers = users.Chunk(25);
            foreach(var userchunk in chunkedUsers) {
                StillAlive();
                var mutatedUsers = new ConcurrentBag<DBUser>();
                // Network calls only below - no DB scope held while awaiting EggIncApi.
                await Parallel.ForEachAsync(userchunk, new ParallelOptions { MaxDegreeOfParallelism = 3 }, async (user, token) => {
                    try {
                        var userMutated = false;
                        foreach(var account in user.EggIncAccounts.Where(x => !string.IsNullOrEmpty(x.Id) && x.Id.StartsWith("EI") && x.LastGrade != Contract.Types.PlayerGrade.GradeUnset)) {
                            var r = await EggIncApi.Post<ContractPlayerInfo, BasicRequestInfo>(new BasicRequestInfo(), account.Id);
                            if(r is null) {
                                _logger.LogWarning("Null response for {user} ({account})", user.DiscordUsername, account.Id);
                                continue;
                            }
                            if(r.Status == ContractPlayerInfo.Types.Status.Complete)
                                userMutated |= GradeSync.ApplyGradeChange(user, account, r.Grade, setPromotionTime: true, guardUnset: true, _logger);
                        }
                        if(userMutated) mutatedUsers.Add(user);
                    } catch(Exception e) {
                        _bugSnag.Notify(e);
                        _logger.LogError(e, "Error checking for grade update for {user}", user.DiscordUsername);
                    }
                });

                if(mutatedUsers.IsEmpty) continue;
                using var saveScope = _provider.CreateScope();
                var saveDb = saveScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                foreach(var user in mutatedUsers) {
                    await saveDb.DBUsers.Where(c => c.Id == user.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(c => c._contractRegistrationByte, user._contractRegistrationByte), CancellationToken.None);
                }
            }
        }
    }
}
