using Cronos;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using Ei;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class UserCXPUpdater(IServiceProvider provider) : _UpdaterBase<UserCXPUpdater>(_runTime, provider) {
#if DEBUG
        private static readonly CronExpression _runTime = CronExpression.Parse("* * * * *");
#else
        private static readonly CronExpression _runTime = CronExpression.Parse("0 9 * * MON,WED,FRI");
#endif

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            //Get a list of all users that are a part of a guild
#if DEBUG
            var users = await _db.DBUsers.AsQueryable().Where(x => x.DiscordId == 273621777119313921).ToListAsync(CancellationToken.None);
#else
            var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId > 0).ToListAsync(CancellationToken.None);
#endif

            //Loop through each user in the DB
            var chunkSize = 25;
            var count = 0;
            var userChunks = users.Chunk(chunkSize);
            var random = new Random();
            var userIDs = users.SelectMany(x => x.EggIncAccounts.Select(y => y.Id)).OrderBy(x => random.Next()).ToList();
            _logger.LogInformation("Getting scores");
            var existingScores = await _db.UserCsHistoryEntries.ToListAsync(CancellationToken.None);
            _logger.LogInformation("Finished Getting scores");
            foreach(var userchunk in userChunks) {
                await WaitOnCoopsBeingCreated(cancellationToken);
                if(cancellationToken.IsCancellationRequested) break;
                StillAlive();
                var scoresToAdd = new List<UserCsHistoryEntry>();
                var skipped = 0;
                await Parallel.ForEachAsync(userchunk, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken }, async (user, cancellationToken) => {
                    //Loop through each account of the user
                    foreach(var account in user.EggIncAccounts.Where(x => x.LastGrade != Ei.Contract.Types.PlayerGrade.GradeUnset)) {
                        if(cancellationToken.IsCancellationRequested) break;
                        try {

                            //Get every score of the user's contracts
                            var scores = await EggIncApi.Post<MyContracts, BasicRequestInfo>(new BasicRequestInfo(), account.Id);

                            if(scores?.Contracts is null) {
                                _logger.LogWarning("Unable to get scores for {user} {account}", user.DiscordUsername, account.Id);
                                continue;
                            }
                            // Egg Inc moves a completed contract from Contracts into Archive once fully
                            // closed/graced, which can happen before this job's next Mon/Wed/Fri run.
                            // Scanning only Contracts silently drops the score for anything that's already
                            // archived by run time, leaving redo-Leggacy threshold checks with no score.
                            foreach(var score in scores.Contracts.Concat(scores.Archive ?? [])) {
                                //Max length for coop because of weird names
                                var coopIdentifier = score.CoopIdentifier.Length > 100 ? score.CoopIdentifier[..100] : score.CoopIdentifier;
                                //Get the score from existing ones
                                var existingScore = existingScores.FirstOrDefault(x => x.ContractIdentifier == score.Contract.Identifier && x.CoopIdentifier == coopIdentifier && x.EggIncId == account.Id);

                                //Check if a score for this contract already exists
                                if(existingScore is null) {
                                    //If it doesn't exist, add a new one
                                    scoresToAdd.Add(new UserCsHistoryEntry(score.Contract.Identifier, coopIdentifier, score.Evaluation.Cxp, account.Id));
                                } else if(existingScore.Cxp != score.Evaluation.Cxp) {
                                    //If it does, update the score and coop name, and bump Created so a
                                    //changed score (e.g. a later replay observed under the same coop
                                    //identifier) still sorts as the most recent play.
                                    existingScore.Cxp = score.Evaluation.Cxp;
                                    existingScore.Created = DateTimeOffset.UtcNow;
                                }
                            }
                        } catch(Exception ex) {
                            _bugSnag.Notify(ex);
                            _logger.LogError(ex, "Error with {user} {account}", user.DiscordUsername, account.Id);
                        }
                    }
                });
                _db.UserCsHistoryEntries.AddRange(scoresToAdd);
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogInformation("Saving Changes {count}/{total}, skipped {skipped}", (++count * chunkSize), users.Count, skipped);
            }
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
