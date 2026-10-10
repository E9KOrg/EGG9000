using EGG9000.Common.Database;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.IO;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageDictionaryTests {
        private static readonly byte[] ZstdDictionaryMagic = [0x37, 0xA4, 0x30, 0xEC];

        [TestMethod]
        public void Registry_IdsAreUniqueAndNonZero() {
            var ids = StorageDictionary.Registry.Select(d => d.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            Assert.IsFalse(ids.Contains(StorageDictionary.None));
        }

        [TestMethod]
        public void Registry_EveryDictionaryLoadsAsZstdDictionary() {
            foreach(var dictionary in new[] { StorageDictionary.Accounts1, StorageDictionary.CoopStatus1 }) {
                var bytes = dictionary.Bytes;
                Assert.IsTrue(bytes.Length > 1024, dictionary.ResourceName);
                CollectionAssert.AreEqual(ZstdDictionaryMagic, bytes.Take(4).ToArray(), dictionary.ResourceName);
                Assert.AreNotEqual(0u, BitConverter.ToUInt32(bytes, 4), dictionary.ResourceName);
            }
        }

        [TestMethod]
        public void Get_KnownId_ReturnsRegisteredInstance() {
            Assert.AreSame(StorageDictionary.Accounts1, StorageDictionary.Get(StorageDictionary.Accounts1.Id));
            Assert.AreSame(StorageDictionary.CoopStatus1, StorageDictionary.Get(StorageDictionary.CoopStatus1.Id));
        }

        [TestMethod]
        public void Get_UnknownId_Throws() {
            Assert.ThrowsExactly<InvalidDataException>(() => StorageDictionary.Get(200));
        }

        [TestMethod]
        public void PinnedIds_NeverChange() {
            Assert.AreEqual(1, StorageDictionary.Accounts1.Id);
            Assert.AreEqual("accounts-1.zdict", StorageDictionary.Accounts1.ResourceName);
            Assert.AreEqual(2, StorageDictionary.CoopStatus1.Id);
            Assert.AreEqual("coopstatus-1.zdict", StorageDictionary.CoopStatus1.ResourceName);
        }

        [TestMethod]
        public void DefaultStrategies_UseZstdWithTheirDictionaries() {
            Assert.AreEqual(StorageCompressionAlgorithm.Zstd, StorageCompressionStrategy.AccountGraph.Algorithm);
            Assert.AreSame(StorageDictionary.Accounts1, StorageCompressionStrategy.AccountGraph.Dictionary);
            Assert.AreEqual(StorageCompressionAlgorithm.Zstd, StorageCompressionStrategy.CoopStatus.Algorithm);
            Assert.AreSame(StorageDictionary.CoopStatus1, StorageCompressionStrategy.CoopStatus.Dictionary);
        }
    }
}
