using EGG9000.Common.Database;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

using static Ei.Contract.Types;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ArchivedFarmSlotWidthTests {
        private static readonly MessagePackSerializerOptions Plain =
            StorageMessagePack.Options.WithCompression(MessagePackCompression.None);

        [MessagePackObject]
        public class ByteShape {
            [Key(0)] public string? CoopId { get; set; }
            [Key(1)] public string? ContractId { get; set; }
            [Key(2)] public float TimeAccepted { get; set; }
            [Key(3)] public bool Completed { get; set; }
            [Key(4)] public byte? League { get; set; }
            [Key(5)] public byte PEPossible { get; set; }
            [Key(6)] public byte PEGained { get; set; }
            [Key(7)] public float ContributionAmount { get; set; }
            [Key(8)] public PlayerGrade Grade { get; set; }
            [Key(9)] public float EvaluationCxp { get; set; }
            [Key(10)] public byte NumGoalsAchieved { get; set; }
            [Key(11)] public List<string>? ReportedUUIDs { get; set; }
        }

        [MessagePackObject]
        public class PreNarrowingShape {
            [Key(0)] public string? CoopId { get; set; }
            [Key(1)] public string? ContractId { get; set; }
            [Key(2)] public float TimeAccepted { get; set; }
            [Key(3)] public bool Completed { get; set; }
            [Key(4)] public byte? League { get; set; }
            [Key(5)] public uint PEPossible { get; set; }
            [Key(6)] public uint PEGained { get; set; }
            [Key(7)] public float ContributionAmount { get; set; }
            [Key(8)] public PlayerGrade Grade { get; set; }
            [Key(9)] public float EvaluationCxp { get; set; }
            [Key(10)] public byte NumGoalsAchieved { get; set; }
            [Key(11)] public List<string>? ReportedUUIDs { get; set; }
        }

        private static CustomArchivedFarms Current(uint pe) => new() {
            CoopId = "coop-width", ContractId = "contract-width", TimeAccepted = 1_650_000_000f, Completed = true,
            League = 1, PEPossible = pe, PEGained = pe, ContributionAmount = 12.5f, Grade = PlayerGrade.GradeAa,
            EvaluationCxp = 3.25f, NumGoalsAchieved = 3, ReportedUUIDs = ["a", "b"]
        };

        private static ByteShape Narrow(byte pe) => new() {
            CoopId = "coop-width", ContractId = "contract-width", TimeAccepted = 1_650_000_000f, Completed = true,
            League = 1, PEPossible = pe, PEGained = pe, ContributionAmount = 12.5f, Grade = PlayerGrade.GradeAa,
            EvaluationCxp = 3.25f, NumGoalsAchieved = 3, ReportedUUIDs = ["a", "b"]
        };

        private static PreNarrowingShape Legacy(uint pe) => new() {
            CoopId = "coop-width", ContractId = "contract-width", TimeAccepted = 1_650_000_000f, Completed = true,
            League = 1, PEPossible = pe, PEGained = pe, ContributionAmount = 12.5f, Grade = PlayerGrade.GradeAa,
            EvaluationCxp = 3.25f, NumGoalsAchieved = 3, ReportedUUIDs = ["a", "b"]
        };

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(127)]
        [DataRow(128)]
        [DataRow(255)]
        public void UintSlots_WriteSameBytesAsByteSlots_Below256(int pe) {
            var widened = MessagePackSerializer.Serialize(Current((uint)pe), Plain);
            var narrow = MessagePackSerializer.Serialize(Narrow((byte)pe), Plain);
            CollectionAssert.AreEqual(narrow, widened);
        }

        [TestMethod]
        [DataRow(700u)]
        [DataRow(1245u)]
        public void PreNarrowingBlob_ReadsWithWidenedSlots(uint pe) {
            var stored = MessagePackSerializer.Serialize(Legacy(pe), Plain);
            var farm = MessagePackSerializer.Deserialize<CustomArchivedFarms>(stored, Plain);
            Assert.AreEqual(pe, farm.PEPossible);
            Assert.AreEqual(pe, farm.PEGained);
            Assert.AreEqual("coop-width", farm.CoopId);
            Assert.AreEqual(3, farm.NumGoalsAchieved);
        }

        [TestMethod]
        public void PreNarrowingBlob_OverflowsByteSlots() {
            var stored = MessagePackSerializer.Serialize(Legacy(700), Plain);
            var thrown = Assert.ThrowsExactly<MessagePackSerializationException>(() => MessagePackSerializer.Deserialize<ByteShape>(stored, Plain));
            Exception root = thrown;
            while(root.InnerException is not null) root = root.InnerException;
            Assert.IsInstanceOfType<OverflowException>(root);
        }

        [TestMethod]
        public void WidenedBlob_RoundTripsThroughStorageCodec() {
            var list = new List<CustomArchivedFarms> { Current(700), Current(5) };
            var back = StorageCodec.Unpack<List<CustomArchivedFarms>>(StorageCodec.Pack(list));
            Assert.AreEqual(700u, back[0].PEPossible);
            Assert.AreEqual(5u, back[1].PEGained);
        }
    }
}
