using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Database {
    public static class StorageDictionaryLoader {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly object Gate = new();
        private static bool _loaded;

        public static void PersistEmbedded(ApplicationDbContext db) {
            foreach(var (dictionary, corpus) in new[] { (StorageDictionary.Accounts1, StorageDictionaryRow.AccountsCorpus), (StorageDictionary.CoopStatus1, StorageDictionaryRow.CoopStatusCorpus) }) {
                try {
                    int id = dictionary.Id;
                    var bytes = dictionary.Bytes;
                    var inserted = db.Database.ExecuteSql($"""
                        INSERT INTO "StorageDictionaries" ("Id", "Corpus", "Fingerprint", "TrainedAt", "EvaluatedAt", "SampleCount", "HoldoutBytesPlain", "HoldoutBytesActive", "HoldoutBytesCandidate", "Bytes", "Active")
                        SELECT {id}, {corpus}, 'embedded', now(), now(), 0, 0, 0, 0, {bytes}, NOT EXISTS (SELECT 1 FROM "StorageDictionaries" WHERE "Corpus" = {corpus} AND "Active")
                        ON CONFLICT ("Id") DO NOTHING
                        """);
                    if(inserted > 0)
                        Logger.Info("Embedded storage dictionary {Id} persisted to StorageDictionaries.", id);
                } catch(Exception e) {
                    Logger.Error(e, "Embedded storage dictionary {Id} could not be persisted.", dictionary.Id);
                }
            }
        }

        public static int Load(ApplicationDbContext db) {
            PersistEmbedded(db);
            List<StorageDictionaryRow> rows;
            try {
                rows = db.StorageDictionaries.AsNoTracking().OrderBy(r => r.Id).ToList();
            } catch(Exception e) {
                Logger.Error(e, "Storage dictionaries could not be read; continuing with the embedded dictionaries only.");
                return -1;
            }
            var applied = 0;
            foreach(var row in rows) {
                try {
                    Apply(row);
                    applied++;
                } catch(Exception e) {
                    Logger.Error(e, "Storage dictionary row {Id} ({Corpus}) rejected.", row.Id, row.Corpus);
                }
            }
            return applied;
        }

        public static void EnsureLoaded(ApplicationDbContext db) {
            lock(Gate) {
                if(_loaded)
                    return;
                _loaded = Load(db) >= 0;
            }
        }

        public static void Refresh(ApplicationDbContext db) {
            if(_loaded)
                Load(db);
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
    }
}
