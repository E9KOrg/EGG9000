using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public static class AccountOwnership {
        public readonly record struct Row(Guid Id, ulong DiscordId, string EggIncIds, byte[] AccountsBlob);

        public static Row? FindOwner(IEnumerable<Row> rows, IReadOnlySet<Guid> removedFrom, string eggIncId) {
            foreach(var row in rows) {
                if(removedFrom.Contains(row.Id)) continue;
                var projected = DBUser.FromAccountColumns(row.EggIncIds, row.AccountsBlob);
                projected.Id = row.Id;
                projected.DiscordId = row.DiscordId;
                if(projected.EggIncAccounts.Any(a => string.Equals(a.Id, eggIncId, StringComparison.OrdinalIgnoreCase)))
                    return row;
            }
            return null;
        }
    }
}
