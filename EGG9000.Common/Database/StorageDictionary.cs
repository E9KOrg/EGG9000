using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EGG9000.Common.Database {
    public sealed class StorageDictionary {
        public const byte None = 0;
        public const byte Accounts1Id = 1;
        public const byte CoopStatus1Id = 2;

        private static readonly ConcurrentDictionary<byte, StorageDictionary> All = new();

        private StorageDictionary(byte id, string resourceName, byte[] bytes) {
            Id = id;
            ResourceName = resourceName;
            Bytes = bytes;
        }

        public byte Id { get; }
        public string ResourceName { get; }
        public byte[] Bytes { get; }

        public static IReadOnlyList<StorageDictionary> Registry {
            get {
                return [.. All.Values.OrderBy(d => d.Id)];
            }
        }

        public static StorageDictionary Get(byte id) {
            return TryGet(id, out var dictionary) ? dictionary : throw new InvalidDataException($"Unknown storage dictionary id {id}.");
        }

        public static bool TryGet(byte id, out StorageDictionary dictionary) {
            return All.TryGetValue(id, out dictionary);
        }

        public static StorageDictionary Register(byte id, string name, byte[] bytes) {
            ArgumentNullException.ThrowIfNull(bytes);
            if(id == None)
                throw new ArgumentOutOfRangeException(nameof(id), "Storage dictionary id 0 means no dictionary.");
            var candidate = new StorageDictionary(id, name, bytes);
            var existing = All.GetOrAdd(id, candidate);
            if(!ReferenceEquals(existing, candidate) && !existing.Bytes.AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException($"Storage dictionary id {id} is already registered as '{existing.ResourceName}' with different bytes.");
            return existing;
        }
    }
}
