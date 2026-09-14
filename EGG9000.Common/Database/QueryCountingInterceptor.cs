using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore.Diagnostics;

using System;
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
        private static readonly char[] _verbSeparators = [' ', '\t', '\r', '\n', '(', ';'];

        internal static bool IsWrite(string commandText, CommandSource source) {
            var verb = LeadingVerb(commandText);
            if(verb is "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "COPY" or "TRUNCATE" or "CREATE" or "ALTER" or "DROP")
                return true;
            if(verb is "SELECT" or "WITH" or "SHOW" or "EXPLAIN")
                return false;
            return source is CommandSource.SaveChanges or CommandSource.Migrations;
        }

        private static string LeadingVerb(string text) {
            if(string.IsNullOrEmpty(text)) return string.Empty;
            var i = 0;
            while(i < text.Length) {
                if(char.IsWhiteSpace(text[i])) { i++; continue; }
                if(text[i] == '-' && i + 1 < text.Length && text[i + 1] == '-') {
                    var nl = text.IndexOf('\n', i);
                    if(nl < 0) return string.Empty;
                    i = nl + 1;
                    continue;
                }
                break;
            }
            if(i >= text.Length) return string.Empty;
            var end = text.IndexOfAny(_verbSeparators, i);
            if(end < 0) end = text.Length;
            return text[i..end].ToUpperInvariant();
        }

        private static void Record(DbCommand command, CommandExecutedEventData eventData) {
            RuntimeMetrics.AddDbQueries();
            RuntimeMetrics.RecordDbCommand(eventData.Duration, IsWrite(command?.CommandText, eventData.CommandSource));
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) {
            Record(command, eventData);
            return base.ReaderExecuted(command, eventData, result);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default) {
            Record(command, eventData);
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) {
            Record(command, eventData);
            return base.NonQueryExecuted(command, eventData, result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default) {
            Record(command, eventData);
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override object ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object result) {
            Record(command, eventData);
            return base.ScalarExecuted(command, eventData, result);
        }

        public override ValueTask<object> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object result, CancellationToken cancellationToken = default) {
            Record(command, eventData);
            return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) {
            RuntimeMetrics.AddDbFailures();
            base.CommandFailed(command, eventData);
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default) {
            RuntimeMetrics.AddDbFailures();
            return base.CommandFailedAsync(command, eventData, cancellationToken);
        }
    }
}
