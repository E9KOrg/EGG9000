using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

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
        [DataRow(null, 0)]
        [DataRow("0", 0)]
        [DataRow("abc", 0)]
        [DataRow("-5", 0)]
        [DataRow("1000", 1000)]
        public void Options_Parse_BatchDelay(string? delayRaw, int expected) {
            Assert.AreEqual(expected, StorageSweepOptions.Parse("1", delayRaw!).BatchDelayMs);
        }

        [TestMethod]
        public void Accounts_LegacyLz4_CompressOn_Converts_AndRoundTrips() {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = true;
            try {
                var stored = LegacyAccountBytes();
                var outcome = StorageSweepCodec.Accounts(stored);

                Assert.AreEqual(SweepOutcomeKind.Converted, outcome.Kind);
                Assert.IsNull(outcome.Error);
                Assert.IsNotNull(outcome.Bytes);
                Assert.AreEqual(StorageCompression.Marker, outcome.Bytes[0]);
                var accounts = StorageCodec.Unpack<List<EggIncAccount>>(outcome.Bytes);
                Assert.AreEqual("EI0000000000012345", accounts.Single().Id);
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void Accounts_BrotliEnvelope_CompressOn_ConvertsToCurrentStrategy() {
            var plain = MessagePackSerializer.Serialize(BuildAccounts(), StorageMessagePack.Options.WithCompression(MessagePackCompression.None));
            var brotli = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Brotli, rawThreshold: 0));
            Assert.AreEqual((byte)StorageCompressionAlgorithm.Brotli, brotli[1]);

            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = true;
            try {
                var outcome = StorageSweepCodec.Accounts(brotli);

                Assert.AreEqual(SweepOutcomeKind.Converted, outcome.Kind);
                Assert.AreEqual((byte)StorageCompressionStrategy.AccountGraph.Algorithm, outcome.Bytes[1]);
                Assert.AreEqual("EI0000000000012345", StorageCodec.Unpack<List<EggIncAccount>>(outcome.Bytes).Single().Id);
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void Accounts_CurrentStrategyEnvelope_CompressOn_IsCurrent() {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = true;
            try {
                var stored = StorageCodec.Pack(BuildAccounts());
                var outcome = StorageSweepCodec.Accounts(stored);

                Assert.AreEqual(SweepOutcomeKind.Current, outcome.Kind);
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void StalePredicate_Zstd_ChecksMarkerAlgorithmAndDictionary() {
            var predicate = StorageSweep.StalePredicate("\"col\"", new StorageCompressionStrategy(StorageCompressionAlgorithm.Zstd, dictionary: StorageDictionary.Accounts1));

            StringAssert.Contains(predicate, "@marker");
            StringAssert.Contains(predicate, "@algo");
            StringAssert.Contains(predicate, "@dict");
            StringAssert.Contains(predicate, "@raw AND octet_length(\"col\") <= @rawMax");
            StringAssert.Contains(predicate, "COALESCE(CASE WHEN octet_length(\"col\") > 2 THEN get_byte(\"col\", 2) END, -1) = @dict");
        }

        [TestMethod]
        public void StalePredicate_Brotli_DoesNotReadDictionaryByte() {
            var predicate = StorageSweep.StalePredicate("\"col\"", new StorageCompressionStrategy(StorageCompressionAlgorithm.Brotli));

            StringAssert.Contains(predicate, "@algo");
            Assert.IsFalse(predicate.Contains("@dict"));
        }

        [TestMethod]
        public void BatchSql_UsesStrategyPredicate() {
            StringAssert.Contains(StorageSweep.UsersBatchSql, StorageSweep.UsersPredicate);
            StringAssert.Contains(StorageSweep.CoopsBatchSql, StorageSweep.CoopsPredicate);
            StringAssert.Contains(StorageSweep.UsersCountSql, StorageSweep.UsersPredicate);
            StringAssert.Contains(StorageSweep.CoopsCountSql, StorageSweep.CoopsPredicate);
        }

        [TestMethod]
        public void Accounts_LegacyLz4_CompressOff_IsCurrent() {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = false;
            try {
                var stored = LegacyAccountBytes();
                var outcome = StorageSweepCodec.Accounts(stored);

                Assert.AreEqual(SweepOutcomeKind.Current, outcome.Kind);
                Assert.IsNull(outcome.Bytes);
                Assert.IsNull(outcome.Error);
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void Accounts_CorruptBytes_Fails_WithoutBytes() {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = true;
            try {
                var outcome = StorageSweepCodec.Accounts(CorruptAccounts);

                Assert.AreEqual(SweepOutcomeKind.Failed, outcome.Kind);
                Assert.IsNotNull(outcome.Error);
                Assert.IsNull(outcome.Bytes);
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void CoopStatus_LegacyGzipJson_ProtoOn_Converts_AndRoundTrips() {
            var prior = CoopStatusCodec.ProtoWriteEnabled;
            CoopStatusCodec.ProtoWriteEnabled = false;
            try {
                var stored = CoopStatusCodec.Encode(SampleStatus());
                CoopStatusCodec.ProtoWriteEnabled = true;
                var outcome = StorageSweepCodec.CoopStatus(stored);

                Assert.AreEqual(SweepOutcomeKind.Converted, outcome.Kind);
                Assert.IsNotNull(outcome.Bytes);
                Assert.AreEqual(StorageCompression.Marker, outcome.Bytes[0]);
                var decoded = CoopStatusCodec.Decode(outcome.Bytes);
                Assert.AreEqual(2, decoded.Contributors.Count);
                Assert.AreEqual(7200d, decoded.SecondsRemaining);
            } finally {
                CoopStatusCodec.ProtoWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void CoopStatus_LegacyGzipJson_ProtoOff_IsCurrent() {
            var prior = CoopStatusCodec.ProtoWriteEnabled;
            CoopStatusCodec.ProtoWriteEnabled = false;
            try {
                var stored = CoopStatusCodec.Encode(SampleStatus());
                var outcome = StorageSweepCodec.CoopStatus(stored);

                Assert.AreEqual(SweepOutcomeKind.Current, outcome.Kind);
                Assert.IsNull(outcome.Bytes);
            } finally {
                CoopStatusCodec.ProtoWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void CoopStatus_CorruptBytes_Fails_WithoutBytes() {
            var prior = CoopStatusCodec.ProtoWriteEnabled;
            CoopStatusCodec.ProtoWriteEnabled = true;
            try {
                var outcome = StorageSweepCodec.CoopStatus(CorruptCoopStatus);

                Assert.AreEqual(SweepOutcomeKind.Failed, outcome.Kind);
                Assert.IsNotNull(outcome.Error);
                Assert.IsNull(outcome.Bytes);
            } finally {
                CoopStatusCodec.ProtoWriteEnabled = prior;
            }
        }
    }
}
