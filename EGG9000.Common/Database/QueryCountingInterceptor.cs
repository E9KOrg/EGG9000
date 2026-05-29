using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore.Diagnostics;

using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    /// <summary>
    /// Counts every executed DB command (reader / non-query / scalar, sync and async)
    /// into <see cref="RuntimeMetrics.DbQueries"/> for runtime reporting. Stateless and
    /// thread safe, so a single instance can be shared across all contexts.
    /// </summary>
    public sealed class QueryCountingInterceptor : DbCommandInterceptor {
        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) {
            RuntimeMetrics.AddDbQueries();
            return base.ReaderExecuted(command, eventData, result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default) {
            RuntimeMetrics.AddDbQueries();
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) {
            RuntimeMetrics.AddDbQueries();
            return base.NonQueryExecuted(command, eventData, result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default) {
            RuntimeMetrics.AddDbQueries();
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override object ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object result) {
            RuntimeMetrics.AddDbQueries();
            return base.ScalarExecuted(command, eventData, result);
        }

        public override ValueTask<object> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object result, CancellationToken cancellationToken = default) {
            RuntimeMetrics.AddDbQueries();
            return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
