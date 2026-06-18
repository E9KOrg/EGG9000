using System;
using System.Collections;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EGG9000.Common.Database {
    // Npgsql 'timestamp with time zone' rejects any DateTimeOffset whose offset is not UTC.
    // The model value converter normalizes mapped-column writes, but it does NOT reach every
    // query parameter - a stray DateTimeOffset.Now in a LINQ Where/OrderBy, a raw SQL literal,
    // or an array param crashes the command before the converter ever runs. This interceptor is
    // the last line of defense: it rewrites every command parameter to UTC right before execution,
    // so a local-offset value can never reach Npgsql. The instant is preserved.
    public sealed class UtcDateTimeOffsetCommandInterceptor : DbCommandInterceptor {
        public static readonly UtcDateTimeOffsetCommandInterceptor Instance = new();

        private static void Normalize(DbCommand command) {
            foreach(DbParameter parameter in command.Parameters)
                parameter.Value = NormalizeValue(parameter.Value);
        }

        private static object NormalizeValue(object value) {
            switch(value) {
                case DateTimeOffset dto when dto.Offset != TimeSpan.Zero:
                    return dto.ToUniversalTime();
                // Npgsql array/list params (e.g. WHERE x = ANY(@p)). Mutate in place; same instant.
                case IList list when value is not byte[] and not string:
                    for(int i = 0; i < list.Count; i++)
                        if(list[i] is DateTimeOffset element && element.Offset != TimeSpan.Zero)
                            list[i] = element.ToUniversalTime();
                    return list;
                default:
                    return value;
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) {
            Normalize(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) {
            Normalize(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result) {
            Normalize(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) {
            Normalize(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result) {
            Normalize(command);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default) {
            Normalize(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
