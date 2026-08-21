using EGG9000.Common.Database;
using EGG9000.Common.Proto;

using Google.Protobuf;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class CustomBackupParentDerivationTests {
        private static readonly MessagePackSerializerOptions Lz4 =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        private static readonly FrozenSet<Ei.Contract> EmptyContracts = new List<Ei.Contract>().ToFrozenSet();

        private static Ei.Backup BuildBackup() {
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
                    NumDailyGiftsCollected = 12,
                    GoldenEggsEarned = 500,
                    GoldenEggsSpent = 200,
                    PiggyBank = 33,
                    HyperloopStation = true,
                    MaxEggReached = Ei.Egg.Medical,
                    EpicResearch = { new Ei.Backup.Types.ResearchItem { Id = "soul_eggs", Level = 8 } },
                    EggMedalLevel = { 1u, 2u, 3u },
                    MaxFarmSizeReached = { 10UL, 0UL, 20UL },
                    News = { new Ei.Backup.Types.NewsHeadline() },
                    Achievements = { new Ei.Backup.Types.AchievementInfo() },
                    Boosts = { new Ei.Backup.Types.OwnedBoost() }
                },
                Settings = new Ei.Backup.Types.Settings { LastBackupTime = 1_700_000_500 },
                Stats = new Ei.Backup.Types.Stats {
                    NumPrestiges = 6,
                    DroneTakedowns = 9,
                    DroneTakedownsElite = 3,
                    NumPiggyBreaks = 2
                },
                Artifacts = new Ei.Backup.Types.Artifacts { CraftingXp = 77.5 },
                Virtue = new Ei.Backup.Types.Virtue {
                    Resets = 5,
                    ShiftCount = 9,
                    EggsDelivered = { 1.5, 2.5 },
                    EovEarned = { 4u, 6u },
                    Afx = new Ei.Backup.Types.Artifacts { CraftingXp = 12.5 }
                },
                Contracts = new Ei.MyContracts(),
                ArtifactsDb = new Ei.ArtifactsDB()
            };
            backup.Farms.Add(new Ei.Backup.Types.Simulation { FarmType = Ei.FarmType.Empty });
            return backup;
        }

        private static void AssertDerivedFromBuildBackup(CustomBackup backup) {
            Assert.AreEqual("EI0000000000012345", backup.EggIncId);
            Assert.AreEqual("ProtoName", backup.UserName);
            Assert.AreEqual(1700000500L, backup.LastBackupTime);
            Assert.AreEqual(1, backup.EpicResearch.Count);
            Assert.AreEqual("soul_eggs", backup.EpicResearch[0].Id);
            Assert.AreEqual(8u, backup.EpicResearch[0].Level);
            Assert.AreEqual((ushort)3, backup.PermitLevel);
            Assert.AreEqual((ushort)11, backup.EggsOfProphecy);
            Assert.AreEqual(5432.25, backup.SoulEggs);
            Assert.AreEqual(1.75, backup.CurrentMultiplier);
            Assert.AreEqual(6UL, backup.NumPrestiges);
            Assert.AreEqual(12u, backup.NumDailyGiftsCollected);
            CollectionAssert.AreEqual(new List<uint> { 1u, 2u, 3u }, backup.EggMedalLevel);
            Assert.AreEqual(500UL, backup.GoldenEggsEarned);
            Assert.AreEqual(200UL, backup.GoldenEggsSpent);
            Assert.AreEqual(33UL, backup.PiggyBank);
            Assert.AreEqual(9UL, backup.DroneTakedowns);
            Assert.AreEqual(3UL, backup.DroneTakedownsElite);
            Assert.AreEqual(2UL, backup.NumPiggyBreaks);
            Assert.IsTrue(backup.HyperloopPurchased);
            Assert.AreEqual((byte)41, backup.ClientVersion);
            Assert.AreEqual(Ei.Egg.Medical, backup.MaxEggReached);
            Assert.AreEqual(10UL, backup.MaxFarmSizeReached[Ei.Egg.Edible]);
            Assert.AreEqual(20UL, backup.MaxFarmSizeReached[Ei.Egg.Medical]);
            Assert.IsFalse(backup.MaxFarmSizeReached.ContainsKey(Ei.Egg.Superfood));
            Assert.IsTrue(backup.HasDeviceId);
            Assert.AreEqual("device-proto-1", backup.DeviceId);
            Assert.AreEqual(77.5, backup.CraftingXP);
            CollectionAssert.AreEqual(new double[] { 1.5, 2.5 }, backup.VirtueEggsDelivered);
            Assert.AreEqual(5u, backup.Resets);
            Assert.AreEqual(9u, backup.ShiftCount);
            CollectionAssert.AreEqual(new uint[] { 4u, 6u }, backup.EovEarned);
            Assert.IsFalse(backup.NoAliasInLatestBackup);
        }

        [TestMethod]
        public void BackCompat_ConvertedMembers_ReturnLegacyValues_WhenEiBackupBytesNull() {
            var backup = new CustomBackup {
                EggIncId = "EI-legacy-0001",
                UserName = "LegacyName",
                LastBackupTime = 1_650_000_000,
                EpicResearch = [new CustomResearch { Id = "soul_eggs", Level = 4 }],
                PermitLevel = 2,
                EggsOfProphecy = 6,
                SoulEggs = 999.5,
                CurrentMultiplier = 1.2,
                NumPrestiges = 3,
                NumDailyGiftsCollected = 7,
                EggMedalLevel = [9u, 8u],
                GoldenEggsEarned = 44,
                GoldenEggsSpent = 11,
                PiggyBank = 5,
                DroneTakedowns = 2,
                DroneTakedownsElite = 1,
                NumPiggyBreaks = 3,
                HyperloopPurchased = true,
                ClientVersion = 12,
                MaxEggReached = Ei.Egg.Tachyon,
                MaxFarmSizeReached = new Dictionary<Ei.Egg, ulong> { [Ei.Egg.Tachyon] = 55 },
                HasDeviceId = true,
                DeviceId = "legacy-device",
                CraftingXP = 21.5,
                VirtueEggsDelivered = [3.3, 4.4],
                Resets = 8,
                ShiftCount = 2,
                EovEarned = [1u, 2u],
                NoAliasInLatestBackup = true
            };

            var bytes = MessagePackSerializer.Serialize(backup, Lz4);
            var back = MessagePackSerializer.Deserialize<CustomBackup>(bytes, Lz4);

            Assert.IsNull(back.EiBackupBytes);
            Assert.AreEqual("EI-legacy-0001", back.EggIncId);
            Assert.AreEqual("LegacyName", back.UserName);
            Assert.AreEqual(1_650_000_000L, back.LastBackupTime);
            Assert.AreEqual(1, back.EpicResearch.Count);
            Assert.AreEqual("soul_eggs", back.EpicResearch[0].Id);
            Assert.AreEqual((ushort)2, back.PermitLevel);
            Assert.AreEqual((ushort)6, back.EggsOfProphecy);
            Assert.AreEqual(999.5, back.SoulEggs);
            Assert.AreEqual(1.2, back.CurrentMultiplier);
            Assert.AreEqual(3UL, back.NumPrestiges);
            Assert.AreEqual(7u, back.NumDailyGiftsCollected);
            CollectionAssert.AreEqual(new List<uint> { 9u, 8u }, back.EggMedalLevel);
            Assert.AreEqual(44UL, back.GoldenEggsEarned);
            Assert.AreEqual(11UL, back.GoldenEggsSpent);
            Assert.AreEqual(5UL, back.PiggyBank);
            Assert.AreEqual(2UL, back.DroneTakedowns);
            Assert.AreEqual(1UL, back.DroneTakedownsElite);
            Assert.AreEqual(3UL, back.NumPiggyBreaks);
            Assert.IsTrue(back.HyperloopPurchased);
            Assert.AreEqual((byte)12, back.ClientVersion);
            Assert.AreEqual(Ei.Egg.Tachyon, back.MaxEggReached);
            Assert.AreEqual(55UL, back.MaxFarmSizeReached[Ei.Egg.Tachyon]);
            Assert.IsTrue(back.HasDeviceId);
            Assert.AreEqual("legacy-device", back.DeviceId);
            Assert.AreEqual(21.5, back.CraftingXP);
            CollectionAssert.AreEqual(new double[] { 3.3, 4.4 }, back.VirtueEggsDelivered);
            Assert.AreEqual(8u, back.Resets);
            Assert.AreEqual(2u, back.ShiftCount);
            CollectionAssert.AreEqual(new uint[] { 1u, 2u }, back.EovEarned);
            Assert.IsTrue(back.NoAliasInLatestBackup);
        }

        [TestMethod]
        public void Truncation_OldBlobWithoutEiBackupBytesKey_FallsBackToLegacyValues() {
            var backup = new CustomBackup(BuildBackup(), EmptyContracts);

            var bytes = MessagePackSerializer.Serialize(backup, MessagePackSerializerOptions.Standard);
            var reader = new MessagePackReader(bytes);
            var count = reader.ReadArrayHeader();
            Assert.IsTrue(count >= 53);

            var buffer = new ArrayBufferWriter<byte>();
            var writer = new MessagePackWriter(buffer);
            writer.WriteArrayHeader(52);
            for(var i = 0; i < 52; i++) {
                writer.WriteRaw(reader.ReadRaw());
            }
            writer.Flush();

            var truncated = MessagePackSerializer.Deserialize<CustomBackup>(buffer.WrittenMemory, MessagePackSerializerOptions.Standard);

            Assert.IsNull(truncated.EiBackupBytes);
            AssertDerivedFromBuildBackup(truncated);
        }

        [TestMethod]
        public void Derivation_ConvertedMembers_EqualEiBackupValues() {
            var backup = new CustomBackup(BuildBackup(), EmptyContracts);

            AssertDerivedFromBuildBackup(backup);
        }

        [TestMethod]
        public void Derivation_IgnoresLegacyFieldWrites_WhileEiBackupBytesSet() {
            var backup = new CustomBackup(BuildBackup(), EmptyContracts);

            backup.NumPrestiges = 999;
            backup.SoulEggs = -1;
            backup.HyperloopPurchased = false;
            backup.EpicResearch = [new CustomResearch { Id = "overridden", Level = 0 }];
            backup.MaxEggReached = Ei.Egg.Edible;

            Assert.AreEqual(6UL, backup.NumPrestiges);
            Assert.AreEqual(5432.25, backup.SoulEggs);
            Assert.IsTrue(backup.HyperloopPurchased);
            Assert.AreEqual(1, backup.EpicResearch.Count);
            Assert.AreEqual("soul_eggs", backup.EpicResearch[0].Id);
            Assert.AreEqual(Ei.Egg.Medical, backup.MaxEggReached);
        }

        [TestMethod]
        public void NoAliasInLatestBackup_DerivesTrue_WhenEiBackupUserNameEmpty_IgnoringLegacyField() {
            var backupWithNoAlias = new Ei.Backup {
                UserId = "backup-user-no-alias",
                UserName = "",
                Game = new Ei.Backup.Types.Game(),
                Artifacts = new Ei.Backup.Types.Artifacts(),
                Contracts = new Ei.MyContracts(),
                ArtifactsDb = new Ei.ArtifactsDB()
            };

            var backup = new CustomBackup(backupWithNoAlias, EmptyContracts);
            backup.NoAliasInLatestBackup = false;

            Assert.IsTrue(backup.NoAliasInLatestBackup);
        }

        [TestMethod]
        public void RoundTrip_ConvertedMembersAndEiBackupBytes_SurviveMessagePack() {
            var backup = new CustomBackup(BuildBackup(), EmptyContracts);

            var bytes = MessagePackSerializer.Serialize(backup, Lz4);
            var back = MessagePackSerializer.Deserialize<CustomBackup>(bytes, Lz4);

            CollectionAssert.AreEqual(backup.EiBackupBytes, back.EiBackupBytes);
            AssertDerivedFromBuildBackup(back);
        }

        [TestMethod]
        public void TrimmedBytes_ClearsNotStoredFields_KeepsGameStatsVirtue() {
            var backup = new CustomBackup(BuildBackup(), EmptyContracts);

            var trimmed = Ei.Backup.Parser.ParseFrom(backup.EiBackupBytes);

            Assert.AreEqual(0, trimmed.Farms.Count);
            Assert.IsNull(trimmed.Contracts);
            Assert.IsNull(trimmed.ArtifactsDb);
            Assert.IsNotNull(trimmed.Game);
            Assert.AreEqual(3u, trimmed.Game.PermitLevel);
            Assert.AreEqual(11UL, trimmed.Game.EggsOfProphecy);
            Assert.AreEqual(0, trimmed.Game.News.Count);
            Assert.AreEqual(0, trimmed.Game.Achievements.Count);
            Assert.AreEqual(0, trimmed.Game.Boosts.Count);
            Assert.IsNotNull(trimmed.Stats);
            Assert.AreEqual(6UL, trimmed.Stats.NumPrestiges);
            Assert.IsNotNull(trimmed.Virtue);
            Assert.AreEqual(5u, trimmed.Virtue.Resets);
            Assert.AreEqual(9u, trimmed.Virtue.ShiftCount);
            Assert.IsNull(trimmed.Virtue.Afx);
        }

        [TestMethod]
        public void TrimmedBytes_TypeWithoutNotStored_RoundTripsWhole() {
            var evaluation = new Ei.ContractEvaluation {
                ContractIdentifier = "contract-untrimmed",
                CoopIdentifier = "coop-untrimmed",
                Cxp = 123.5,
                Grade = Ei.Contract.Types.PlayerGrade.GradeAa,
                CoopSize = 10,
                ChickenRunsSent = 4,
                GiftTokensSent = 7,
                TeamworkScore = 0.75,
                SeasonId = "season-1",
                Issues = { Ei.ContractEvaluation.Types.PoorBehavior.LowContribution },
                Notes = { "note-a", "note-b" }
            };

            var parsed = Ei.ContractEvaluation.Parser.ParseFrom(StorageTrimmer.TrimmedBytes(evaluation));

            Assert.AreEqual(evaluation, parsed);
        }

        [TestMethod]
        public void EmptyBackup_NullGame_ProducesDefaultsAndNullEiBackupBytes() {
            var backup = new CustomBackup(new Ei.Backup(), EmptyContracts);

            Assert.IsTrue(backup.EmptyBackup);
            Assert.IsNull(backup.EiBackupBytes);
            Assert.IsNull(backup.Farms);
            Assert.IsNull(backup.EggIncId);
            Assert.AreEqual((ushort)0, backup.PermitLevel);
            Assert.AreEqual(0d, backup.SoulEggs);
            Assert.AreEqual((byte)0, backup.ClientVersion);
            Assert.AreEqual(0UL, backup.NumPrestiges);
            Assert.IsFalse(backup.HyperloopPurchased);
        }

        [TestMethod]
        public void EiBackupBytes_ResettingInvalidatesDerivedCollectionCaches() {
            var backup = new CustomBackup();

            var first = new Ei.Backup {
                Game = new Ei.Backup.Types.Game {
                    EpicResearch = { new Ei.Backup.Types.ResearchItem { Id = "soul_eggs", Level = 1 } },
                    EggMedalLevel = { 1u },
                    MaxFarmSizeReached = { 5UL }
                },
                Virtue = new Ei.Backup.Types.Virtue {
                    EggsDelivered = { 1.1 },
                    EovEarned = { 2u }
                }
            };
            var second = new Ei.Backup {
                Game = new Ei.Backup.Types.Game {
                    EpicResearch = { new Ei.Backup.Types.ResearchItem { Id = "prophecy_bonus", Level = 9 } },
                    EggMedalLevel = { 7u, 8u },
                    MaxFarmSizeReached = { 0UL, 40UL }
                },
                Virtue = new Ei.Backup.Types.Virtue {
                    EggsDelivered = { 9.9, 8.8 },
                    EovEarned = { 3u, 4u }
                }
            };

            backup.EiBackupBytes = first.ToByteArray();
            Assert.AreEqual("soul_eggs", backup.EpicResearch[0].Id);
            CollectionAssert.AreEqual(new List<uint> { 1u }, backup.EggMedalLevel);
            Assert.AreEqual(5UL, backup.MaxFarmSizeReached[Ei.Egg.Edible]);
            CollectionAssert.AreEqual(new double[] { 1.1 }, backup.VirtueEggsDelivered);
            CollectionAssert.AreEqual(new uint[] { 2u }, backup.EovEarned);

            backup.EiBackupBytes = second.ToByteArray();
            Assert.AreEqual("prophecy_bonus", backup.EpicResearch[0].Id);
            CollectionAssert.AreEqual(new List<uint> { 7u, 8u }, backup.EggMedalLevel);
            Assert.AreEqual(40UL, backup.MaxFarmSizeReached[Ei.Egg.Superfood]);
            Assert.IsFalse(backup.MaxFarmSizeReached.ContainsKey(Ei.Egg.Edible));
            CollectionAssert.AreEqual(new double[] { 9.9, 8.8 }, backup.VirtueEggsDelivered);
            CollectionAssert.AreEqual(new uint[] { 3u, 4u }, backup.EovEarned);
        }
    }
}
