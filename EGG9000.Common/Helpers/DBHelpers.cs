using EGG9000.Common.Database;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class DBHelpers {

        public static async Task<(bool, int)> SaveChangesAsyncRetry(this ApplicationDbContext db, int retryCount = 1, CancellationToken cancellationToken = default) {
            var currentRetry = 0;
            while(true) {
                try {
                    return (true, await db.SaveChangesAsync(cancellationToken));
                } catch(Exception) {
                    if(currentRetry >= retryCount) return (false, currentRetry);
                    currentRetry++;
                }
            }
        }

    }
}
