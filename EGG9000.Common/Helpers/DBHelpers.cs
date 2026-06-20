using EGG9000.Common.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class DBHelpers {

        extension(ApplicationDbContext db) {
            /// <returns>
            ///     A boolean representing whether or not the save operation was a success before the maximum number of retries was reached
            ///     An int, representing the number of state entries written to the database, in the case of a sucess, -1 otherwise
            /// </returns>
            public async Task<(bool, int)> SaveChangesAsyncRetry(int retryCount = 1, ILogger logger = null, CancellationToken cancellationToken = default) {
                var currentRetry = 0;
                while(true) {
                    try {
                        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(1));
                        return (true, await db.SaveChangesAsync(cancellationToken));
                    } catch(Exception e) {
                        if(currentRetry++ > retryCount) {
                            logger?.LogError(e, "SaveChangesAsyncRetry Max Retries Reached");
                            return (false, -1);
                        }
                        if(e is NpgsqlException { IsTransient: true } || e.InnerException is NpgsqlException { IsTransient: true }) {
                            logger?.LogWarning("SaveChangesAsyncRetry: Postgres transient error, retrying ({retry}/{max})", currentRetry, retryCount);
                        }
                        await Task.Delay(100, cancellationToken);
                    }
                }
            }
        }
    }
}
