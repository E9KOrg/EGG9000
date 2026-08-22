using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageCodecTests {
        private const string ContractId = "contract-storage-codec";

        private static readonly MessagePackSerializerOptions Plain =
            StorageMessagePack.Options.WithCompression(MessagePackCompression.None);

        private static (Ei.Backup Backup, FrozenSet<Ei.Contract> Contracts) BuildBackup() {
            var contract = new Ei.Contract { Identifier = ContractId };
            var localContract = new Ei.LocalContract {
                Contract = contract,
                ContractIdentifier = ContractId,
                CoopIdentifier = "coop-storage-codec",
                League = 1,
                TimeAccepted = 1_650_000_000,
                CoopSharedEndTime = 1_650_500_000,
                BoostsUsed = 3,
                Grade = Ei.Contract.Types.PlayerGrade.GradeAa,
                NumGoalsAchieved = 2
            };
            var simulation = new Ei.Backup.Types.Simulation {
                ContractId = ContractId,
                FarmType = Ei.FarmType.Contract,
                EggsPaidFor = 55.5,
                NumChickens = 12345,
                EggType = Ei.Egg.RocketFuel,
                SilosOwned = 4
            };

            var backup = new Ei.Backup {
                UserId = "backup-user-1",
                EiUserId = "EI0000000000012345",
                UserName = "ProtoName",
                DeviceId = "device-proto-1",
                Version = 41,
                Game = new Ei.Backup.Types.Game {
                    PermitLevel = 3,
                    EggsOfProphecy = 11,
                    SoulEggsD = 5432.25,
                    CurrentMultiplier = 1.75,
                    GoldenEggsEarned = 500,
                    HyperloopStation = true,
                    MaxEggReached = Ei.Egg.Medical,
                    EpicResearch = { new Ei.Backup.Types.ResearchItem { Id = "soul_eggs", Level = 8 } }
                },
                Settings = new Ei.Backup.Types.Settings { LastBackupTime = 1_700_000_500 },
                Stats = new Ei.Backup.Types.Stats { NumPrestiges = 6, DroneTakedowns = 9 },
                Artifacts = new Ei.Backup.Types.Artifacts { CraftingXp = 77.5 },
                Contracts = new Ei.MyContracts { Contracts = { localContract } },
                ArtifactsDb = new Ei.ArtifactsDB()
            };
            backup.Farms.Add(simulation);

            return (backup, new List<Ei.Contract> { contract }.ToFrozenSet());
        }

        private static CustomBackup BuildLegacyBackup() {
            return new CustomBackup {
                EggIncId = "EI-legacy-0001",
                UserName = "LegacyName",
                LastBackupTime = 1_650_000_000,
                EpicResearch = [new CustomResearch { Id = "soul_eggs", Level = 4 }],
                PermitLevel = 2,
                SoulEggs = 999.5,
                NumPrestiges = 3,
                HyperloopPurchased = true,
                MaxEggReached = Ei.Egg.Tachyon,
                Resets = 8
            };
        }

        private static List<EggIncAccount> BuildAccounts() {
            var (proto, contracts) = BuildBackup();
            return [
                new EggIncAccount { Id = "EI-legacy-0001", Backup = BuildLegacyBackup() },
                new EggIncAccount { Id = "EI0000000000012345", Backup = new CustomBackup(proto, contracts) }
            ];
        }

        private static void AssertAccountsShape(List<EggIncAccount> accounts) {
            Assert.AreEqual(2, accounts.Count);

            var legacy = accounts[0].Backup;
            Assert.IsNull(legacy.EiBackupBytes);
            Assert.AreEqual("EI-legacy-0001", legacy.EggIncId);
            Assert.AreEqual("LegacyName", legacy.UserName);
            Assert.AreEqual(999.5, legacy.SoulEggs);
            Assert.AreEqual(Ei.Egg.Tachyon, legacy.MaxEggReached);

            var derived = accounts[1].Backup;
            Assert.IsNotNull(derived.EiBackupBytes);
            Assert.AreEqual("EI0000000000012345", derived.EggIncId);
            Assert.AreEqual("ProtoName", derived.UserName);
            Assert.AreEqual(5432.25, derived.SoulEggs);
            Assert.AreEqual(ContractId, derived.Farms.Single().ContractId);
            Assert.AreEqual(1, derived.ArchivedFarms.Count);
        }

        private static void AssertEnvelope(byte[] bytes) {
            Assert.IsNotNull(bytes);
            Assert.IsTrue(bytes.Length >= 2);
            Assert.AreEqual(StorageCompression.Marker, bytes[0]);
            Assert.AreEqual((byte)StorageCompressionAlgorithm.Brotli, bytes[1]);
        }

        private static byte[] GzipStoredFixture(List<EggIncAccount> accounts) {
            var plain = MessagePackSerializer.Serialize(accounts, Plain);
            using var output = new System.IO.MemoryStream();
            using(var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                gzip.Write(plain, 0, plain.Length);
            return output.ToArray();
        }

        [TestMethod]
        public void Pack_DefaultOff_IsByteIdenticalToLegacyLz4_AndUnpacks() {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = false;
            try {
                var accounts = BuildAccounts();
                var packed = StorageCodec.Pack(accounts);
                var expected = MessagePackSerializer.Serialize(accounts, DBUser.lz4Options);
                CollectionAssert.AreEqual(expected, packed);
                AssertAccountsShape(StorageCodec.Unpack<List<EggIncAccount>>(packed));
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void Pack_ToggleOn_WritesEnvelope_AndRoundTrips() {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = true;
            try {
                var accounts = BuildAccounts();
                var packed = StorageCodec.Pack(accounts);
                AssertEnvelope(packed);
                AssertAccountsShape(StorageCodec.Unpack<List<EggIncAccount>>(packed));
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void Unpack_GzipStoredBytes_ReadsIntact() {
            AssertAccountsShape(StorageCodec.Unpack<List<EggIncAccount>>(GzipStoredFixture(BuildAccounts())));
        }

        [TestMethod]
        public void Unpack_LegacyLz4Bytes_ReadsIntact() {
            var accounts = BuildAccounts();
            var stored = MessagePackSerializer.Serialize(accounts, DBUser.lz4Options);
            AssertAccountsShape(StorageCodec.Unpack<List<EggIncAccount>>(stored));
        }

        [TestMethod]
        public void Unpack_PlainMsgpackBytes_ReadsIntact() {
            var accounts = BuildAccounts();
            var stored = MessagePackSerializer.Serialize(accounts, Plain);
            AssertAccountsShape(StorageCodec.Unpack<List<EggIncAccount>>(stored));
        }

        [TestMethod]
        public void Unpack_GzipMagicFollowedByGarbage_ThrowsMessagePackSerializationException() {
            var corrupt = new byte[] { 0x1F, 0x8B, 0x99, 0x11, 0x22, 0x33, 0x44 };
            Assert.ThrowsExactly<MessagePackSerializationException>(() => StorageCodec.Unpack<List<EggIncAccount>>(corrupt));
        }

        [TestMethod]
        public void Unpack_EnvelopeFollowedByGarbage_ThrowsMessagePackSerializationException() {
            var corrupt = new byte[] { StorageCompression.Marker, (byte)StorageCompressionAlgorithm.Brotli, 0x99, 0x11, 0x22 };
            Assert.ThrowsExactly<MessagePackSerializationException>(() => StorageCodec.Unpack<List<EggIncAccount>>(corrupt));
        }

        [TestMethod]
        public void DBUser_ToggleOn_WritesEnvelopeColumn_SecondUserRehydrates() {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = true;
            try {
                var (proto, contracts) = BuildBackup();
                var user = new DBUser();
                user.EggIncAccounts = [new EggIncAccount { Id = "EI0000000000012345", Backup = new CustomBackup(proto, contracts) }];

                AssertEnvelope(user._contractRegistrationByte);

                var rehydrated = new DBUser { _contractRegistrationByte = user._contractRegistrationByte };
                var accounts = rehydrated.EggIncAccounts;

                Assert.AreEqual(1, accounts.Count);
                var account = accounts.Single();
                Assert.AreEqual("EI0000000000012345", account.Id);
                Assert.IsNotNull(account.Backup);
                Assert.AreEqual("EI0000000000012345", account.Backup.EggIncId);
                Assert.AreEqual(5432.25, account.Backup.SoulEggs);
                Assert.AreEqual(ContractId, account.Backup.Farms.Single().ContractId);
                Assert.AreEqual(1, account.Backup.ArchivedFarms.Count);
                Assert.AreEqual("device-proto-1", account.DeviceID);
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void DBUser_ToggleOn_LegacyLz4Row_StillReads() {
            var prior = StorageCodec.CompressWriteEnabled;
            StorageCodec.CompressWriteEnabled = true;
            try {
                var stored = MessagePackSerializer.Serialize(BuildAccounts(), DBUser.lz4Options);
                var user = new DBUser { _contractRegistrationByte = stored };
                AssertAccountsShape(user.EggIncAccounts);
            } finally {
                StorageCodec.CompressWriteEnabled = prior;
            }
        }

        [TestMethod]
        public void DBUser_CorruptGzipColumn_YieldsEmptyAccountsWithoutThrowing() {
            var user = new DBUser { _contractRegistrationByte = [0x1F, 0x8B, 0x99, 0x11, 0x22] };

            var accounts = user.EggIncAccounts;

            Assert.IsNotNull(accounts);
            Assert.AreEqual(0, accounts.Count);
        }

        [TestMethod]
        public void DBUser_CorruptEnvelopeColumn_YieldsEmptyAccountsWithoutThrowing() {
            var user = new DBUser { _contractRegistrationByte = [StorageCompression.Marker, (byte)StorageCompressionAlgorithm.Brotli, 0x99, 0x11, 0x22] };

            var accounts = user.EggIncAccounts;

            Assert.IsNotNull(accounts);
            Assert.AreEqual(0, accounts.Count);
        }
    }
}
