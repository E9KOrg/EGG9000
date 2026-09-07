using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace EGG9000.Common.Database {
    public sealed class StorageDictionary {
        public const byte None = 0;

        public static readonly StorageDictionary Accounts1 = new(1, "accounts-1.zdict");
        public static readonly StorageDictionary CoopStatus1 = new(2, "coopstatus-1.zdict");

        private static readonly StorageDictionary[] All = [Accounts1, CoopStatus1];

        private readonly Lazy<byte[]> _bytes;

        private StorageDictionary(byte id, string resourceName) {
            Id = id;
            ResourceName = resourceName;
            _bytes = new Lazy<byte[]>(() => Load(resourceName), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public byte Id { get; }
        public string ResourceName { get; }
        public byte[] Bytes => _bytes.Value;

        public static IReadOnlyList<StorageDictionary> Registry => All;

        public static StorageDictionary Get(byte id) {
            return All.FirstOrDefault(d => d.Id == id) ?? throw new InvalidDataException($"Unknown storage dictionary id {id}.");
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
