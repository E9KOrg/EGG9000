using Cronos;

using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Ei;

using Google.Protobuf.WellKnownTypes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace EGG9000.Bot.Automated {
    public class UserCxpUpdater : _UpdaterBase<UserCxpUpdater> {
#if DEBUG
        //public UserCxpUpdater(
        //    IServiceProvider provider
        //) : base(CronExpression.Parse("0 9 * * MON,WED,FRI"), provider) {
        //}
        public UserCxpUpdater(
            IServiceProvider provider
        ) : base(TimeSpan.MaxValue, TimeSpan.Zero, provider) {
        }
#else

        public UserCxpUpdater(
            IServiceProvider provider
        ) : base(CronExpression.Parse("0 9 * * MON,WED,FRI"), provider) {
        }
#endif

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            //Get a list of all users that are a part of a guild
#if DEBUG
            var users = await _db.DBUsers.AsQueryable().Where(x => x.DiscordId == 273621777119313921).ToListAsync();
#else
            var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId > 0).ToListAsync();
#endif

            //Loop through each user in the DB
            var chunkSize = 25;
            var count = 0;
            var userChunks = users.Chunk(chunkSize);
            var random = new Random();
            var userIDs = users.SelectMany(x => x.EggIncAccounts.Select(y => y.Id)).OrderBy(x => random.Next()).ToList();
            _logger.LogInformation("Getting scores");
            //var existingScores = await _db.UserCsHistoryEntries.Where(x => userIDs.Contains(x.EggIncId)).ToListAsync();
            var existingScores = await _db.UserCsHistoryEntries.ToListAsync();
            _logger.LogInformation("Finished Getting scores");
            foreach(var userchunk in userChunks) {
                this.StillAlive();
                var scoresToAdd = new List<UserCsHistoryEntry>();
                var skipped = 0;
                await Parallel.ForEachAsync(userchunk, new ParallelOptions { MaxDegreeOfParallelism = 3 }, async (user, cancellationToken) => {
                    //Loop through each account of the user
                    foreach(var account in user.EggIncAccounts.Where(x => x.LastGrade != Ei.Contract.Types.PlayerGrade.GradeUnset)) {
                        try {

                            //Get every score of the user's contracts
                            var scores = await ContractsAPI.Post<MyContracts, BasicRequestInfo>(new BasicRequestInfo(), account.Id);

                            if(scores?.Contracts is null) {
                                _logger.LogWarning("Unable to get scores for {user} {account}", user.DiscordUsername, account.Id);
                                continue;
                            }
                            foreach(var score in scores.Contracts) {
                                //Max length for coop because of weird names
                                var coopIdentifier = score.CoopIdentifier.Length > 100 ? score.CoopIdentifier.Substring(0, 100) : score.CoopIdentifier;
                                //Get the score from existing ones
                                var existingScore = existingScores.FirstOrDefault(x => x.ContractIdentifier == score.Contract.Identifier && x.CoopIdentifier == coopIdentifier && x.EggIncId == account.Id);

                                //Check if a score for this contract already exists
                                if(existingScore is null) {
                                    //If it doesn't exist, add a new one
                                    scoresToAdd.Add(new UserCsHistoryEntry(score.Contract.Identifier, coopIdentifier, score.Evaluation.Cxp, account.Id));
                                } else if(existingScore.Cxp != score.Evaluation.Cxp) {
                                    //If it does, update the score and coop name. Should only happen if the dev changes the scoring algorithm
                                    existingScore.Cxp = score.Evaluation.Cxp;
                                }
                            }
                        } catch(Exception ex) {
                            _bugsnag.Notify(ex);
                            _logger.LogError(ex, "Error with {user} {account}", user.DiscordUsername, account.Id);
                        }
                    }
                });
                _db.UserCsHistoryEntries.AddRange(scoresToAdd);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Saving Changes {count}/{total}, skipped {skipped}", (++count * chunkSize), users.Count, skipped);
            }
            await _db.SaveChangesAsync();
        }
    }
}
