using EGG9000.Common.Database;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class DBHelpers {

        /// <returns>
        ///     A boolean representing whether or not the save operation was a success before the maximum number of retries was reached
        ///     An int, representing the number of state entries written to the database, in the case of a sucess, -1 otherwise
        /// </returns>
        public static async Task<(bool, int)> SaveChangesAsyncRetry(this ApplicationDbContext db, int retryCount = 1, CancellationToken cancellationToken = default, ILogger logger = null) {
            var currentRetry = 0;
            while(true) {
                try {
                    return (true, await db.SaveChangesAsync(cancellationToken));
                } catch(Exception e) {
                    // Never retry on DB timeout or connection failure - fail fast so the caller can release resources
                    // and avoid compounding load on an already-stressed database
                    if(e is SqlException { Number: -2 or 20 } || e.InnerException is SqlException { Number: -2 or 20 }) {
                        if(logger is not null) {
                            logger.LogWarning("SaveChangesAsyncRetry: SQL timeout/connection error, not retrying");
                        }
                        return (false, -1);
                    }
                    //If we reached max retry count, exit
                    if(currentRetry++ > retryCount) {
                        if(logger is not null) {
                            logger.LogError(e, "SaveChangesAsyncRetry Max Retries Reached");
                        }
                        return (false, -1);
                    }
                    //Slight delay between attempts
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

    }
}
