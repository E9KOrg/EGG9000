using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Common.Helpers;
using Ei;
using Humanizer;
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Cronos;

namespace EGG9000.Bot.Automated {
    public class HandleGradeChanges(IServiceProvider provider) : _UpdaterBase<HandleGradeChanges>(CronExpression.Parse("30 10,18,23 * * 1,3,5"), provider) {

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var users = await _db.DBUsers.Where(x => x.GuildId == 656455567858073601).ToListAsync(cancellationToken);
            var chunkedUsers = users.Chunk(25);
            foreach(var userchunk in chunkedUsers) {
                StillAlive();
                await Parallel.ForEachAsync(userchunk, new ParallelOptions { MaxDegreeOfParallelism = 3 }, async (user, token) => {
                    try {
                        foreach(var account in user.EggIncAccounts.Where(x => !string.IsNullOrEmpty(x.Id) && x.Id.StartsWith("EI") && x.LastGrade != Ei.Contract.Types.PlayerGrade.GradeUnset)) {
                            var r = await ContractsAPI.Post<ContractPlayerInfo, BasicRequestInfo>(new BasicRequestInfo(), account.Id);
                            if(r is null) {
                                _logger.LogWarning("Null response for {user} ({account})", user.DiscordUsername, account.Id);
                                continue;
                            }
                            if(r?.Grade != account.LastGrade) {
                                _logger.LogInformation("Update grade for {user} ({account}) Prev {LastGrade} New {NewGrade}", user.DiscordUsername, account.Backup?.UserName, account.LastGrade, r.Grade);
                                account.PromotionTime = DateTimeOffset.Now;
                                account.LastGrade = r.Grade;
                                user.UpdateAccounts();
                            }
                        }
                    } catch(Exception e) {
                        _bugsnag.Notify(e);
                        _logger.LogError(e, "Error checking for grade update");
                    }
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
