using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class BlobAccessorTests {

        private static readonly MessagePackSerializerOptions Lz4Options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        [TestMethod]
        public void MessagePack_RoundTrip() {
            var writer = new MessagePackBlobAccessor<ContributionInfoCompact>(Lz4Options);
            var stored = writer.Set(new ContributionInfoCompact { UserName = "Tester", SoulPower = 20 }, null);

            var value = new MessagePackBlobAccessor<ContributionInfoCompact>(Lz4Options).Get(stored);

            Assert.AreEqual("Tester", value.UserName);
            Assert.AreEqual(20d, value.SoulPower);
        }

        [TestMethod]
        public void MessagePack_NullColumn_ReturnsNull_WithoutFallback() {
            Assert.IsNull(new MessagePackBlobAccessor<ContributionInfoCompact>(Lz4Options).Get(null));
        }

        [TestMethod]
        public void MessagePack_NullColumn_UsesAndCachesFallback() {
            var accessor = new MessagePackBlobAccessor<List<ContributionInfoCompact>>(Lz4Options, () => []);
            var first = accessor.Get(null);
            var second = accessor.Get(null);

            Assert.IsNotNull(first);
            Assert.AreSame(first, second);
        }

        [TestMethod]
        public void MessagePack_Set_SuppressesUnchangedWrites() {
            var accessor = new MessagePackBlobAccessor<ContributionInfoCompact>(Lz4Options);
            var value = new ContributionInfoCompact { UserName = "Tester" };
            var stored = accessor.Set(value, null);
            var again = accessor.Set(value, stored);

            Assert.AreSame(stored, again);
        }

        [TestMethod]
        public void MessagePack_Set_WritesWhenChanged() {
            var accessor = new MessagePackBlobAccessor<ContributionInfoCompact>(Lz4Options);
            var stored = accessor.Set(new ContributionInfoCompact { UserName = "One" }, null);
            var changed = accessor.Set(new ContributionInfoCompact { UserName = "Two" }, stored);

            Assert.AreNotSame(stored, changed);
        }

        [TestMethod]
        public void MessagePack_OptionsRespected() {
            var value = new ContributionInfoCompact { UserName = new string('a', 2048), ContributionAmount = 1e12 };
            var lz4 = new MessagePackBlobAccessor<ContributionInfoCompact>(Lz4Options).Set(value, null);
            var plain = new MessagePackBlobAccessor<ContributionInfoCompact>().Set(value, null);

            Assert.IsTrue(lz4.Length < plain.Length);
            Assert.AreEqual(value.UserName, new MessagePackBlobAccessor<ContributionInfoCompact>().Get(plain).UserName);
            Assert.AreEqual(value.UserName, new MessagePackBlobAccessor<ContributionInfoCompact>(Lz4Options).Get(lz4).UserName);
        }

        [TestMethod]
        public void MessagePack_EmptyColumn_UsesFallback() {
            var value = new MessagePackBlobAccessor<List<ContributionInfoCompact>>(Lz4Options, () => []).Get([]);

            Assert.IsNotNull(value);
            Assert.AreEqual(0, value.Count);
        }

        [TestMethod]
        public void MessagePack_ParsedNull_UsesFallback() {
            var value = new MessagePackBlobAccessor<List<ContributionInfoCompact>>(Lz4Options, () => []).Get([MessagePackCode.Nil]);

            Assert.IsNotNull(value);
        }

        [TestMethod]
        public void MessagePack_Prime_SeedsCache() {
            var accessor = new MessagePackBlobAccessor<ContributionInfoCompact>(Lz4Options);
            var value = new ContributionInfoCompact { UserName = "Primed" };
            accessor.Prime(value);

            Assert.AreSame(value, accessor.Get(null));
        }

        [TestMethod]
        public void Codec_RoundTrip() {
            var stored = NewCodecAccessor().Set(new ContributionInfoCompact { UserName = "Tester" }, null);
            var value = NewCodecAccessor().Get(stored);

            Assert.AreEqual("Tester", value.UserName);
        }

        [TestMethod]
        public void Codec_NullColumn_ReturnsNull() {
            Assert.IsNull(NewCodecAccessor().Get(null));
        }

        [TestMethod]
        public void Codec_Set_SuppressesUnchangedWrites() {
            var accessor = NewCodecAccessor();
            var value = new ContributionInfoCompact { UserName = "Tester" };
            var stored = accessor.Set(value, null);

            Assert.AreSame(stored, accessor.Set(value, stored));
        }

        private static CodecBlobAccessor<ContributionInfoCompact> NewCodecAccessor()
            => new(stored => MessagePackSerializer.Deserialize<ContributionInfoCompact>(stored, Lz4Options),
                value => MessagePackSerializer.Serialize(value, Lz4Options));

        [TestMethod]
        public void Json_RoundTrip() {
            var stored = new JsonBlobAccessor<VirtueSnapshotStats>().Set(new VirtueSnapshotStats { TeTotal = 5 }, null);
            var value = new JsonBlobAccessor<VirtueSnapshotStats>().Get(stored);

            Assert.AreEqual(5, value.TeTotal);
        }

        [TestMethod]
        public void Json_NullColumn_UsesFallbackJson() {
            var value = new JsonBlobAccessor<List<ulong>>("[]").Get(null);

            Assert.IsNotNull(value);
            Assert.AreEqual(0, value.Count);
        }

        [TestMethod]
        public void Json_ParsedNull_UsesFactory() {
            var accessor = new JsonBlobAccessor<VirtueSnapshotStats>("{}", () => new VirtueSnapshotStats());

            Assert.IsNotNull(accessor.Get("null"));
        }

        [TestMethod]
        public void Json_Set_SuppressesUnchangedWrites() {
            var accessor = new JsonBlobAccessor<VirtueSnapshotStats>();
            var value = new VirtueSnapshotStats { TeTotal = 3 };
            var stored = accessor.Set(value, null);
            var again = accessor.Set(value, stored);

            Assert.AreSame(stored, again);
        }

        [TestMethod]
        public void Json_NullColumn_NoFallback_ReturnsNull() {
            Assert.IsNull(new JsonBlobAccessor<VirtueSnapshotStats>().Get(null));
        }
    }
}
