using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Database {
    public static class StorageDictionaryLoader {
        private static readonly object Gate = new();
        private static bool _loaded;

        public static async Task<int> LoadAsync(ApplicationDbContext db, CancellationToken token) {
            return Apply(await db.StorageDictionaries.AsNoTracking().OrderBy(r => r.Id).ToListAsync(token));
        }

        public static int Load(ApplicationDbContext db) {
            return Apply(db.StorageDictionaries.AsNoTracking().OrderBy(r => r.Id).ToList());
        }

        public static void EnsureLoaded(ApplicationDbContext db) {
            lock(Gate) {
                if(_loaded)
                    return;
                Load(db);
                _loaded = true;
            }
        }

        public static StorageDictionary Apply(StorageDictionaryRow row) {
            ArgumentNullException.ThrowIfNull(row);
            if(row.Id is <= StorageDictionary.None or > byte.MaxValue)
                throw new InvalidOperationException($"Storage dictionary row id {row.Id} does not fit the envelope dictionary byte.");
            var dictionary = StorageDictionary.Register((byte)row.Id, $"{row.Corpus}-{row.Id}.db", row.Bytes);
            if(row.Active)
                Activate(row.Corpus, dictionary);
            return dictionary;
        }

        public static void Activate(string corpus, StorageDictionary dictionary) {
            switch(corpus) {
                case StorageDictionaryRow.AccountsCorpus:
                    StorageCompressionStrategy.SetAccountGraph(dictionary);
                    break;
                case StorageDictionaryRow.CoopStatusCorpus:
                    StorageCompressionStrategy.SetCoopStatus(dictionary);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown storage dictionary corpus '{corpus}'.");
            }
        }

        private static int Apply(List<StorageDictionaryRow> rows) {
            foreach(var row in rows)
                Apply(row);
            return rows.Count;
        }
    }
}
