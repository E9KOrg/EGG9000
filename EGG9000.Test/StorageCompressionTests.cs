using EGG9000.Common.Database;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageCompressionTests {

        private static byte[] CompressiblePayload(int length)
            => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("egg-inc-coop-contract-", length / 22 + 1)))[..length];

        [TestMethod]
        public void Brotli_RoundTrips() {
            var plain = CompressiblePayload(4096);
            var stored = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Brotli));

            Assert.AreEqual(StorageCompression.Marker, stored[0]);
            Assert.AreEqual((byte)StorageCompressionAlgorithm.Brotli, stored[1]);
            Assert.IsTrue(stored.Length < plain.Length);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void GZip_RoundTrips() {
            var plain = CompressiblePayload(4096);
            var stored = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.GZip));

            Assert.AreEqual((byte)StorageCompressionAlgorithm.GZip, stored[1]);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void UnderThreshold_StoresRaw() {
            var plain = CompressiblePayload(32);
            var stored = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Brotli, rawThreshold: 64));

            Assert.AreEqual((byte)StorageCompressionAlgorithm.Raw, stored[1]);
            Assert.AreEqual(plain.Length + 2, stored.Length);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void Incompressible_FallsBackToRaw() {
            var random = new Random(9001);
            var plain = new byte[512];
            random.NextBytes(plain);
            var stored = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Brotli));

            Assert.AreEqual((byte)StorageCompressionAlgorithm.Raw, stored[1]);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void RawStrategy_AlwaysStoresRaw() {
            var plain = CompressiblePayload(4096);
            var stored = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Raw));

            Assert.AreEqual((byte)StorageCompressionAlgorithm.Raw, stored[1]);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void IsEnveloped_RejectsForeignFormats() {
            Assert.IsFalse(StorageCompression.IsEnveloped(null));
            Assert.IsFalse(StorageCompression.IsEnveloped([]));
            Assert.IsFalse(StorageCompression.IsEnveloped([0xEB]));
            Assert.IsFalse(StorageCompression.IsEnveloped([0x1F, 0x8B, 0x00]));
            Assert.IsFalse(StorageCompression.IsEnveloped([0x92, 0xC0, 0xC0]));
            Assert.IsFalse(StorageCompression.IsEnveloped([0xE9, 0x1F, 0x8B]));
        }

        [TestMethod]
        public void Decompress_NotEnveloped_Throws() {
            Assert.ThrowsExactly<InvalidDataException>(() => StorageCompression.Decompress([0x1F, 0x8B, 0x00]));
        }

        [TestMethod]
        public void Decompress_UnknownAlgorithm_Throws() {
            Assert.ThrowsExactly<InvalidDataException>(() => StorageCompression.Decompress([StorageCompression.Marker, 0x7F, 0x00]));
        }

        [TestMethod]
        public void Compress_IsDeterministic() {
            var plain = CompressiblePayload(4096);
            var strategy = new StorageCompressionStrategy(StorageCompressionAlgorithm.Brotli);

            CollectionAssert.AreEqual(StorageCompression.Compress(plain, strategy), StorageCompression.Compress(plain, strategy));
        }

        [TestMethod]
        public void Zstd_WithoutDictionary_RoundTrips() {
            var plain = CompressiblePayload(4096);
            var stored = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Zstd));

            Assert.AreEqual(StorageCompression.Marker, stored[0]);
            Assert.AreEqual((byte)StorageCompressionAlgorithm.Zstd, stored[1]);
            Assert.AreEqual(StorageDictionary.None, stored[2]);
            Assert.IsTrue(stored.Length < plain.Length);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void Zstd_WithDictionary_RoundTrips_AndStampsDictionaryId() {
            var plain = CompressiblePayload(4096);
            var stored = StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph);

            Assert.AreEqual((byte)StorageCompressionAlgorithm.Zstd, stored[1]);
            Assert.AreEqual(StorageDictionary.Accounts1.Id, stored[2]);
            Assert.IsTrue(stored.Length < plain.Length);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void Zstd_EachRegisteredDictionary_RoundTrips() {
            var plain = CompressiblePayload(2048);
            foreach(var dictionary in StorageDictionary.Registry) {
                var stored = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Zstd, dictionary: dictionary));
                Assert.AreEqual(dictionary.Id, stored[2], dictionary.ResourceName);
                CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored), dictionary.ResourceName);
            }
        }

        [TestMethod]
        public void Zstd_WrongDictionaryId_Throws() {
            var plain = CompressiblePayload(4096);
            var stored = StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph);
            stored[2] = StorageDictionary.CoopStatus1.Id;

            Assert.ThrowsExactly<InvalidDataException>(() => StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void Zstd_UnknownDictionaryId_Throws() {
            var plain = CompressiblePayload(4096);
            var stored = StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph);
            stored[2] = 200;

            Assert.ThrowsExactly<InvalidDataException>(() => StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void Zstd_MissingDictionaryByte_Throws() {
            Assert.ThrowsExactly<InvalidDataException>(() => StorageCompression.Decompress([StorageCompression.Marker, (byte)StorageCompressionAlgorithm.Zstd]));
        }

        [TestMethod]
        public void Zstd_TruncatedFrame_Throws() {
            var plain = CompressiblePayload(4096);
            var stored = StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph);

            Assert.ThrowsExactly<InvalidDataException>(() => StorageCompression.Decompress(stored[..(stored.Length / 2)]));
        }

        [TestMethod]
        public void Zstd_Compress_IsDeterministic() {
            var plain = CompressiblePayload(4096);

            CollectionAssert.AreEqual(StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph), StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph));
        }

        [TestMethod]
        public void Zstd_UnderThreshold_StoresRaw() {
            var plain = CompressiblePayload(32);
            var stored = StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph);

            Assert.AreEqual((byte)StorageCompressionAlgorithm.Raw, stored[1]);
            Assert.AreEqual(plain.Length + 2, stored.Length);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void Zstd_Incompressible_FallsBackToRaw() {
            var random = new Random(9001);
            var plain = new byte[512];
            random.NextBytes(plain);
            var stored = StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph);

            Assert.AreEqual((byte)StorageCompressionAlgorithm.Raw, stored[1]);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(stored));
        }

        [TestMethod]
        public void Zstd_ParallelRoundTrips_UsePooledCodersSafely() {
            var payloads = Enumerable.Range(0, 256).Select(i => CompressiblePayload(512 + i * 37)).ToArray();

            Parallel.For(0, payloads.Length * 8, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i => {
                var plain = payloads[i % payloads.Length];
                var strategy = i % 2 == 0 ? StorageCompressionStrategy.AccountGraph : StorageCompressionStrategy.CoopStatus;
                CollectionAssert.AreEqual(plain, StorageCompression.Decompress(StorageCompression.Compress(plain, strategy)));
            });
        }

        [TestMethod]
        public void BrotliEnvelope_StillDecodes_UnderZstdDefaults() {
            var plain = CompressiblePayload(4096);
            var brotli = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Brotli));

            Assert.AreEqual((byte)StorageCompressionAlgorithm.Brotli, brotli[1]);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(brotli));
        }
    }
}
