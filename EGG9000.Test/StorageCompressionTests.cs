using EGG9000.Common.Database;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.IO;
using System.Linq;
using System.Text;

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
    }
}
