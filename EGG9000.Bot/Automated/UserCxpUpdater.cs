using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Ei;

using Google.Protobuf.WellKnownTypes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class UserCxpUpdater : _UpdaterBase<UserCxpUpdater> {
        #region DelayCalculations
        /* 
         * Calculations for next runtime - will run at 11 AM EST on M,W,F
        */
        private static readonly int _dayOfWeek = (int)DateTime.Now.DayOfWeek;
        private static readonly int _currentMinute = (60 * DateTime.Now.Hour) + DateTime.Now.Minute;

        //From DayOfWeek doc;
        // Returns the day-of-week part of this DateTime. The returned value
        // is an integer between 0 and 6, where 0 indicates Sunday, 1 indicates
        // Monday, 2 indicates Tuesday, 3 indicates Wednesday, 4 indicates
        // Thursday, 5 indicates Friday, and 6 indicates Saturday.
        //
        private static readonly int _nextRunDayDelay = _dayOfWeek switch {
            0 => 1,
            1 => 0,
            2 => 1,
            3 => 0,
            4 => 1,
            5 => 0,
            6 => 0,
            _ => 0
        };

        // 600 minutes past midnight of system time is 11 AM EST
        private static readonly int _nextRunSecondDelay = 600 - _currentMinute;

        // Determine total time the system should wait before refreshing next
        private static readonly int _nextRunTimeDelay = (60 * 60 * 24 * _nextRunDayDelay) + _nextRunSecondDelay;
        #endregion

        public UserCxpUpdater(
            IServiceProvider provider
        ) : base(TimeSpan.FromDays(1), TimeSpan.FromSeconds(_nextRunTimeDelay), provider) { }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            //Get a list of all users that are a part of a guild
            var users = await _db.DBUsers.AsQueryable().Where(x => x.GuildId > 0).ToListAsync();

            //Loop through each user in the DB
            foreach(var user in users) {

                //Loop through each account of the user
                foreach(var account in user.EggIncAccounts) {

                    //Get every score of the user's contracts
                    var scores = await ContractsAPI.Post<MyContracts, BasicRequestInfo>(new BasicRequestInfo(), account.Id);
                    //Get the existing scores from the database
                    var existingScores = await _db.UserCsHistoryEntries.Where(x => x.EggIncId == account.Id).ToListAsync();

                    foreach(var score in scores.Contracts) {
                        //Get the score from existing ones
                        var existingScore = existingScores.FirstOrDefault(x => x.ContractIdentifier == score.Contract.Identifier && x.CoopIdentifier == score.CoopIdentifier);

                        //Check if a score for this contract already exists
                        if(existingScore is null) {
                            //If it doesn't exist, add a new one
                            _db.UserCsHistoryEntries.Add(new UserCsHistoryEntry(score.Contract.Identifier, score.CoopIdentifier, score.Evaluation.Cxp, account.Id));
                        } else if(existingScore.Cxp != score.Evaluation.Cxp) {
                            //If it does, update the score and coop name. Should only happen if the dev changes the scoring algorithm
                            existingScore.Cxp = score.Evaluation.Cxp;
                        }
                    }
                }
            }
            await _db.SaveChangesAsync();
        }
    }
}
