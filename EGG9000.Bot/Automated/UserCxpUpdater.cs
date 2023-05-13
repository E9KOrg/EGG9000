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

namespace EGG9000.Bot.Automated
{
    public class UserCxpUpdater : _UpdaterBase<UserCxpUpdater>
    {
        #region DelayCalculations
        /* 
         * Calculations for next runtime - will run at 11 AM EST on M,W,F
        */
        private static readonly int _dayOfWeek = (int)DateTime.Now.DayOfWeek;
        private static readonly int _currentMinute = (60*DateTime.Now.Hour) + DateTime.Now.Minute;

        //From DayOfWeek doc;
        // Returns the day-of-week part of this DateTime. The returned value
        // is an integer between 0 and 6, where 0 indicates Sunday, 1 indicates
        // Monday, 2 indicates Tuesday, 3 indicates Wednesday, 4 indicates
        // Thursday, 5 indicates Friday, and 6 indicates Saturday.
        //
        private static readonly int _nextRunDayDelay = _dayOfWeek switch
        {
            0 => 1, 1 => 0, 2 => 1, 3 => 0, 4 => 1, 5 => 0, 6 => 0, _ => 0
        };

        // 600 minutes past midnight of system time is 11 AM EST
        private static readonly int _nextRunSecondDelay = 600 - _currentMinute;

        // Determine total time the system should wait before refreshing next
        private static readonly int _nextRunTimeDelay = (60 * 60 * 24 * _nextRunDayDelay) + _nextRunSecondDelay;
        #endregion

        public UserCxpUpdater(
            IServiceProvider provider
        ) : base(TimeSpan.FromDays(1), TimeSpan.FromSeconds(_nextRunTimeDelay), provider){}

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            //Get a list of all users that have at least one backup, and at least one custombackup in the DB
            var users = await _db.DBUsers.AsQueryable().Where(x => x.Backups.Any() && x._CustomBackups.Any()).ToListAsync();

            //Loop through each user in the DB
            foreach(var user in users) {
                
                //Loop through each account of the user
                foreach(var account in user.EggIncAccounts) {

                    //Get every score of the user's contracts
                    var scores = await ContractsAPI.Post<MyContracts, BasicRequestInfo>(new BasicRequestInfo(), account.Id);
                    //Get the backup for the user's account
                    var backup = user.Backups.AsQueryable().Where(x => x.EggIncId == account.Id).FirstOrDefault();

                    if (backup != null) {
                        foreach(var archivedFarm in backup.ArchivedFarms) {
                            //Get the score from the list
                            var score = scores.Contracts.FirstOrDefault(x => x.Contract.Identifier == archivedFarm.ContractId)?.Evaluation;
                            //Check if a score for this contract already exists in the User's list
                            if(!account.CSHistory.Where(h => h.ContractIdentifier == archivedFarm.ContractId && h.Cxp == score.Cxp).Any()) {
                                //If it doesn't exist, add a new one
                                account.CSHistory.Add(new UserCsHistoryEntry(archivedFarm.ContractId, archivedFarm.CoopId, score.Cxp));
                            } else {
                                //If it does, update the score
                                account.CSHistory.FirstOrDefault(h => h.ContractIdentifier == archivedFarm.ContractId).Cxp = score.Cxp;
                            }
                        }
                    }
                }
            }
            await _db.SaveChangesAsync();
        }
    }
}
