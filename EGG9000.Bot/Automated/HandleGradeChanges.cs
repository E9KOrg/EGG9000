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

namespace EGG9000.Bot.Automated {
    public class HandleGradeChanges : _UpdaterBase<UserSnapShots> {

        public HandleGradeChanges(
            IServiceProvider provider
        ) : base(TimeSpan.FromHours(0.5), TimeSpan.FromMinutes(15), provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var users = await _db.DBUsers.Where(x => x.GuildId == 656455567858073601).ToListAsync();

            var chunkedUsers = users.Chunk(100);
            foreach(var userchunk in chunkedUsers) {
                await Parallel.ForEachAsync(userchunk, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (user, token) => {
                    foreach(var account in user.EggIncAccounts.Where(x => !string.IsNullOrEmpty(x.Id) && x.Id.StartsWith("EI") && x.LastGrade != Ei.Contract.Types.PlayerGrade.GradeUnset)) {
                        var r = await ContractsAPI.Post<ContractPlayerInfo, BasicRequestInfo>(new BasicRequestInfo(), account.Id);
                        if(r is null) {
                            Console.WriteLine($" **  Null response for {user.DiscordUsername} ({account.Id})");
                            continue;
                        }
                        if(r?.Grade != account.LastGrade) {
                            Console.WriteLine($"Update grade for {user.DiscordUsername} ({account.Name}) Prev {account.LastGrade} New {r.Grade}");
                            account.PromotionTime = DateTimeOffset.Now;
                            account.LastGrade = r.Grade;
                            user.UpdateAccounts();
                        }
                    }
                });
                await _db.SaveChangesAsync();
                //Console.WriteLine("Saving Changes");
            }
        }
    }
}
