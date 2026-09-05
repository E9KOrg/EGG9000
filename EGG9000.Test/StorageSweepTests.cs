using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageSweepTests {
        private static readonly byte[] CorruptAccounts = [0xC1, 0xFF, 0x00];
        private static readonly byte[] CorruptCoopStatus = [0x00, 0x01];

        private static List<EggIncAccount> BuildAccounts() {
            return [new EggIncAccount { Id = "EI0000000000012345", Name = "Sweeper" }];
        }

        private static byte[] LegacyAccountBytes() {
            return MessagePackSerializer.Serialize(BuildAccounts(), StorageMessagePack.Options);
        }

        private static Ei.ContractCoopStatusResponse SampleStatus() {
            var status = new Ei.ContractCoopStatusResponse {
                ContractIdentifier = "sweep-contract",
                CoopIdentifier = "sweep-coop",
                TotalAmount = 250_000,
                SecondsRemaining = 7200
            };
            status.Contributors.Add(new Ei.ContractCoopStatusResponse.Types.ContributionInfo { UserId = "EI1", UserName = "One", ContributionAmount = 100_000 });
            status.Contributors.Add(new Ei.ContractCoopStatusResponse.Types.ContributionInfo { UserId = "EI2", UserName = "Two", ContributionAmount = 150_000 });
            return status;
        }

        private static T WithCompressWrite<T>(bool enabled, Func<T> action) {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = enabled;
            try {
                return action();
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        private static T WithProtoWrite<T>(bool enabled, Func<T> action) {
            var prior = CoopStatusCodec.ProtoWriteEnabled;
            CoopStatusCodec.ProtoWriteEnabled = enabled;
            try {
                return action();
            } finally {
                CoopStatusCodec.ProtoWriteEnabled = prior;
            }
        }

        [TestMethod]
        [DataRow("1", true)]
        [DataRow("true", true)]
        [DataRow("TRUE", true)]
        [DataRow(null, false)]
        [DataRow("", false)]
        [DataRow("0", false)]
        public void Options_Parse_Enabled(string? enabledRaw, bool expected) {
            Assert.AreEqual(expected, StorageSweepOptions.Parse(enabledRaw!, null!).Enabled);
        }

        [TestMethod]
        [DataRow(null, 250)]
        [DataRow("0", 0)]
        [DataRow("abc", 250)]
        [DataRow("-5", 0)]
        [DataRow("1000", 1000)]
        public void Options_Parse_BatchDelay(string? delayRaw, int expected) {
            Assert.AreEqual(expected, StorageSweepOptions.Parse("1", delayRaw!).BatchDelayMs);
        }

        [TestMethod]
        public void Accounts_LegacyLz4_CompressOn_Converts_AndRoundTrips() {
            var stored = LegacyAccountBytes();
            var outcome = WithCompressWrite(true, () => StorageSweepCodec.Accounts(stored));

            Assert.AreEqual(SweepOutcomeKind.Converted, outcome.Kind);
            Assert.IsNull(outcome.Error);
            Assert.IsNotNull(outcome.Bytes);
            Assert.AreEqual(StorageCompression.Marker, outcome.Bytes[0]);
            var accounts = StorageCodec.Unpack<List<EggIncAccount>>(outcome.Bytes);
            Assert.AreEqual("EI0000000000012345", accounts.Single().Id);
        }

        [TestMethod]
        public void Accounts_LegacyLz4_CompressOff_IsCurrent() {
            var stored = LegacyAccountBytes();
            var outcome = WithCompressWrite(false, () => StorageSweepCodec.Accounts(stored));

            Assert.AreEqual(SweepOutcomeKind.Current, outcome.Kind);
            Assert.IsNull(outcome.Bytes);
            Assert.IsNull(outcome.Error);
        }

        [TestMethod]
        public void Accounts_CorruptBytes_Fails_WithoutBytes() {
            var outcome = WithCompressWrite(true, () => StorageSweepCodec.Accounts(CorruptAccounts));

            Assert.AreEqual(SweepOutcomeKind.Failed, outcome.Kind);
            Assert.IsNotNull(outcome.Error);
            Assert.IsNull(outcome.Bytes);
        }

        [TestMethod]
        public void CoopStatus_LegacyGzipJson_ProtoOn_Converts_AndRoundTrips() {
            var stored = WithProtoWrite(false, () => CoopStatusCodec.Encode(SampleStatus()));
            var outcome = WithProtoWrite(true, () => StorageSweepCodec.CoopStatus(stored));

            Assert.AreEqual(SweepOutcomeKind.Converted, outcome.Kind);
            Assert.IsNotNull(outcome.Bytes);
            Assert.AreEqual(StorageCompression.Marker, outcome.Bytes[0]);
            var decoded = CoopStatusCodec.Decode(outcome.Bytes);
            Assert.AreEqual(2, decoded.Contributors.Count);
            Assert.AreEqual(7200d, decoded.SecondsRemaining);
        }

        [TestMethod]
        public void CoopStatus_LegacyGzipJson_ProtoOff_IsCurrent() {
            var stored = WithProtoWrite(false, () => CoopStatusCodec.Encode(SampleStatus()));
            var outcome = WithProtoWrite(false, () => StorageSweepCodec.CoopStatus(stored));

            Assert.AreEqual(SweepOutcomeKind.Current, outcome.Kind);
            Assert.IsNull(outcome.Bytes);
        }

        [TestMethod]
        public void CoopStatus_CorruptBytes_Fails_WithoutBytes() {
            var outcome = WithProtoWrite(true, () => StorageSweepCodec.CoopStatus(CorruptCoopStatus));

            Assert.AreEqual(SweepOutcomeKind.Failed, outcome.Kind);
            Assert.IsNotNull(outcome.Error);
            Assert.IsNull(outcome.Bytes);
        }
    }
}
