using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;

using ZstdSharp;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageDictionaryRegistryTests {
        private const byte ReusedId = 250;
        private const byte TrainedId = 251;

        private static readonly MessagePackSerializerOptions Plain = StorageMessagePack.Options.WithCompression(MessagePackCompression.None);

        private static byte[] Sample(int i) {
            return MessagePackSerializer.Serialize(new List<EggIncAccount> {
                new() { Id = $"EI{i:D16}", Name = $"Farmer {i % 17}", Group = (byte)(i % 5), Active = i % 2 == 0, Guild = "palace", LastEB = i * 1.5e12 }
            }, Plain);
        }

        [TestMethod]
        public void Register_NewId_TryGetFinds_AndRoundTrips() {
            var registered = StorageDictionary.Register(ReusedId, "accounts-reuse.db", StorageDictionary.Accounts1.Bytes);

            Assert.IsTrue(StorageDictionary.TryGet(ReusedId, out var found));
            Assert.AreSame(registered, found);
            Assert.AreSame(found, StorageDictionary.Get(ReusedId));
            Assert.IsTrue(StorageDictionary.Registry.Any(d => d.Id == ReusedId));

            var plain = Sample(1);
            var stored = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Zstd, rawThreshold: 0, dictionary: found));
            Assert.AreEqual(ReusedId, stored[2]);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void Register_SameIdSameBytes_IsIdempotent() {
            var first = StorageDictionary.Register(ReusedId, "accounts-reuse.db", StorageDictionary.Accounts1.Bytes);
            var second = StorageDictionary.Register(ReusedId, "other-name.db", [.. StorageDictionary.Accounts1.Bytes]);
            Assert.AreSame(first, second);
        }

        [TestMethod]
        public void Register_SameIdDifferentBytes_Throws() {
            StorageDictionary.Register(ReusedId, "accounts-reuse.db", StorageDictionary.Accounts1.Bytes);
            Assert.ThrowsExactly<InvalidOperationException>(() => StorageDictionary.Register(ReusedId, "clash.db", StorageDictionary.CoopStatus1.Bytes));
        }

        [TestMethod]
        public void Register_ZeroId_Throws() {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StorageDictionary.Register(StorageDictionary.None, "none.db", StorageDictionary.Accounts1.Bytes));
        }

        [TestMethod]
        public void TryGet_UnknownId_False() {
            Assert.IsFalse(StorageDictionary.TryGet(230, out var dictionary));
            Assert.IsNull(dictionary);
        }

        [TestMethod]
        public void TrainedDictionary_RegisteredUnderNewId_RoundTripsThroughEnvelope() {
            var samples = Enumerable.Range(0, 200).Select(Sample).ToList();
            var trained = DictBuilder.TrainFromBuffer(samples, 16384);
            var dictionary = StorageDictionary.Register(TrainedId, "accounts-trained.db", trained);
            var strategy = new StorageCompressionStrategy(StorageCompressionAlgorithm.Zstd, rawThreshold: 0, dictionary: dictionary);

            foreach(var plain in samples.Take(20)) {
                var stored = StorageCompression.Compress(plain, strategy);
                Assert.AreEqual(TrainedId, stored[2]);
                CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
            }
        }
    }
}
