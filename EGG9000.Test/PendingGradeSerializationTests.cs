using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

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
    }
}
