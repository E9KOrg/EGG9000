using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Buffers;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class PendingGradeSerializationTests {
        private static readonly MessagePackSerializerOptions Lz4 =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        [TestMethod]
        public void PendingGrade_RoundTrips() {
            var account = new EggIncAccount {
                Id = "EI0000000000000001",
                LastGrade = G.GradeAa,
                PendingGrade = G.GradeAaa,
                PendingGradeSince = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)
            };

            var bytes = MessagePackSerializer.Serialize(account, Lz4);
            var back = MessagePackSerializer.Deserialize<EggIncAccount>(bytes, Lz4);

            Assert.AreEqual(G.GradeAaa, back.PendingGrade);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), back.PendingGradeSince);
        }

        [TestMethod]
        public void PendingGrade_DefaultsToNull() {
            var account = new EggIncAccount { Id = "EI0000000000000002", LastGrade = G.GradeAa };

            var bytes = MessagePackSerializer.Serialize(account, Lz4);
            var back = MessagePackSerializer.Deserialize<EggIncAccount>(bytes, Lz4);

            Assert.IsNull(back.PendingGrade);
        }

        [TestMethod]
        public void PendingGrade_OldBlobWithoutNewKeys_Deserializes() {
            var account = new EggIncAccount {
                Id = "EI0000000000000003",
                LastGrade = G.GradeAa,
                PendingGrade = G.GradeAaa,
                PendingGradeSince = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)
            };

            var bytes = MessagePackSerializer.Serialize(account, MessagePackSerializerOptions.Standard);
            var reader = new MessagePackReader(bytes);
            var count = reader.ReadArrayHeader();
            Assert.IsTrue(count >= 47);

            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteArrayHeader(45);
            for(var i = 0; i < 45; i++) {
                writer.WriteRaw(reader.ReadRaw());
            }
            writer.Flush();

            var back = MessagePackSerializer.Deserialize<EggIncAccount>(buffer.WrittenMemory, MessagePackSerializerOptions.Standard);

            Assert.AreEqual(G.GradeAa, back.LastGrade);
            Assert.IsNull(back.PendingGrade);
            Assert.AreEqual(default(DateTimeOffset), back.PendingGradeSince);
        }
    }
}
