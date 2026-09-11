using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class CustomBackupVirtueTests {
        private static CustomBackup RoundTripWithNullVirtueEggs() {
            var backup = new CustomBackup {
                EggIncId = "EI0000000000000000",
                UserName = "legacy",
                Farms = [],
                VirtueEggsDelivered = null!
            };
            var bytes = MessagePackSerializer.Serialize(backup, DBUser.lz4Options);
            return MessagePackSerializer.Deserialize<CustomBackup>(bytes, DBUser.lz4Options);
        }

        [TestMethod]
        public void VirtueEggsDelivered_NilSlot_DeserializesToEmptyArray() {
            var back = RoundTripWithNullVirtueEggs();

            Assert.IsNotNull(back.VirtueEggsDelivered);
            Assert.AreEqual(0, back.VirtueEggsDelivered.Length);
        }

        [TestMethod]
        public void EggStats_NilSlot_ReturnsZeroDeliveredStats() {
            var back = RoundTripWithNullVirtueEggs();

            var stats = VirtueHelper.EggStats(back, Ei.Egg.Curiosity);

            Assert.AreEqual(0d, stats.Delivered);
            Assert.AreEqual(0, stats.Level);
            Assert.AreEqual(0, back.EggsOfTruthTotal);
        }
    }
}
