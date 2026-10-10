using EGG9000.Common.Database;

using Google.Protobuf;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class CustomBackupPlayerInfoTests {
        private static readonly MessagePackSerializerOptions Lz4 =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        private static Ei.ContractPlayerInfo SampleInfo() => new() {
            Grade = Ei.Contract.Types.PlayerGrade.GradeAa,
            Status = Ei.ContractPlayerInfo.Types.Status.Complete,
            GradeProgress = 0.42,
            GradeScore = 1234.5,
            TargetGradeScore = 2000,
            SoulPower = 10,
            TargetSoulPower = 20,
            IssueScore = 0,
            LastEvaluationTime = 1_787_000_000,
            LastEvaluationVersion = "cxp-v0.2.0",
            AggregationNotes = "Final grade: GRADE_AA"
        };

        [TestMethod]
        public void ComputedProperties_ReadThroughStoredBytes() {
            var backup = new CustomBackup { LastContractPlayerInfoBytes = SampleInfo().ToByteArray() };

            Assert.AreEqual(0.42, backup.GradeProgress);
            Assert.AreEqual(1234.5, backup.GradeScore);
            Assert.AreEqual(2000d, backup.TargetGradeScore);
            Assert.AreEqual(10d, backup.SoulPower);
            Assert.AreEqual(20d, backup.TargetSoulPower);
            Assert.AreEqual(1_787_000_000d, backup.LastEvaluationTime);
            Assert.AreEqual("cxp-v0.2.0", backup.LastEvaluationVersion);
            Assert.AreEqual("Final grade: GRADE_AA", backup.AggregationNotes);
        }

        [TestMethod]
        public void ComputedProperties_DefaultWhenNoBytesStored() {
            var backup = new CustomBackup();

            Assert.AreEqual(0d, backup.GradeProgress);
            Assert.AreEqual("", backup.AggregationNotes);
            Assert.IsEmpty(backup.Issues);
        }

        [TestMethod]
        public void LastContractPlayerInfoBytes_RoundTripsThroughMessagePack() {
            var backup = new CustomBackup { LastContractPlayerInfoBytes = SampleInfo().ToByteArray() };

            var bytes = MessagePackSerializer.Serialize(backup, Lz4);
            var back = MessagePackSerializer.Deserialize<CustomBackup>(bytes, Lz4);

            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeAa, back.LastContractPlayerInfo.Grade);
            Assert.AreEqual(0.42, back.GradeProgress);
        }
    }
}
