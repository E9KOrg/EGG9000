using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EGG9000.Common.Database {
    public sealed class StorageDictionary {
        public const byte None = 0;

        public static readonly StorageDictionary Accounts1 = new(1, "accounts-1.zdict");
        public static readonly StorageDictionary CoopStatus1 = new(2, "coopstatus-1.zdict");

        private static readonly ConcurrentDictionary<byte, StorageDictionary> All = new(new[] { Accounts1, CoopStatus1 }.ToDictionary(d => d.Id));

        private readonly Lazy<byte[]> _bytes;

        private StorageDictionary(byte id, string resourceName) : this(id, resourceName, () => Load(resourceName)) { }

        private StorageDictionary(byte id, string resourceName, Func<byte[]> load) {
            Id = id;
            ResourceName = resourceName;
            _bytes = new Lazy<byte[]>(load, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public byte Id { get; }
        public string ResourceName { get; }
        public byte[] Bytes {
            get {
                return _bytes.Value;
            }
        }

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
            var candidate = new StorageDictionary(id, name, () => bytes);
            var existing = All.GetOrAdd(id, candidate);
            if(!ReferenceEquals(existing, candidate) && !existing.Bytes.AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException($"Storage dictionary id {id} is already registered as '{existing.ResourceName}' with different bytes.");
            return existing;
        }

        private static byte[] Load(string resourceName) {
            var assembly = typeof(StorageDictionary).Assembly;
            var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + resourceName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Embedded storage dictionary '{resourceName}' not found in {assembly.GetName().Name}.");
            using var stream = assembly.GetManifestResourceStream(name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
