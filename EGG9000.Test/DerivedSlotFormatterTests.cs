using EGG9000.Common.Database;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class DerivedSlotFormatterTests {
        private static readonly MessagePackSerializerOptions Lz4 =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        private const string ContractId = "contract-derived-slot";

        private static (Ei.Backup Backup, FrozenSet<Ei.Contract> Contracts) BuildBackup(string userName = "ProtoName") {
            var contract = new Ei.Contract { Identifier = ContractId };
            var localContract = new Ei.LocalContract {
                Contract = contract,
                ContractIdentifier = ContractId,
                CoopIdentifier = "coop-derived-slot",
                League = 1,
                TimeAccepted = 1_650_000_000,
                Cancelled = true,
                CoopSharedEndTime = 1_650_500_000,
                BoostsUsed = 3,
                Grade = Ei.Contract.Types.PlayerGrade.GradeAa,
                CoopContributionFinalized = true,
                CoopSimulationEndTime = 1_650_600_000,
                NumGoalsAchieved = 2,
                Evaluation = new Ei.ContractEvaluation { Cxp = 44 }
            };
            var simulation = new Ei.Backup.Types.Simulation {
                ContractId = ContractId,
                FarmType = Ei.FarmType.Contract,
                EggsPaidFor = 55.5,
                NumChickens = 12345,
                EggType = Ei.Egg.RocketFuel,
                SilosOwned = 4,
                BoostTokensReceived = 3,
                BoostTokensGiven = 2,
                BoostTokensSpent = 1,
                CashEarned = 5000.5,
                CashSpent = 1200.25,
                TimeCheatDebtDEP = 42,
                TimeCheatsDetected = 1,
                LastStepTime = 3.5,
                CommonResearch = { new Ei.Backup.Types.ResearchItem { Id = "hab_capacity", Level = 6 } },
                TrainLength = { 5u, 6u, 7u },
                Habs = { 10u, 20u, 30u, 40u },
                Vehicles = { 8u, 9u }
            };

            var backup = new Ei.Backup {
                UserId = "backup-user-1",
                EiUserId = "EI0000000000012345",
                UserName = userName,
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
                    MaxFarmSizeReached = { 10UL, 0UL, 20UL }
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
                    EovEarned = { 4u, 6u }
                },
                Contracts = new Ei.MyContracts { Contracts = { localContract } },
                ArtifactsDb = new Ei.ArtifactsDB()
            };
            backup.Farms.Add(simulation);

            return (backup, new List<Ei.Contract> { contract }.ToFrozenSet());
        }

        private static CustomBackup RoundTrip(CustomBackup source, MessagePackSerializerOptions write, MessagePackSerializerOptions read) {
            return MessagePackSerializer.Deserialize<CustomBackup>(MessagePackSerializer.Serialize(source, write), read);
        }

        [TestMethod]
        public void Suppression_DerivedSlots_HoldDefaults_AfterEiBackupBytesCleared() {
            var (proto, contracts) = BuildBackup();
            var back = RoundTrip(new CustomBackup(proto, contracts), StorageMessagePack.Options, StorageMessagePack.Options);

            Assert.AreEqual("EI0000000000012345", back.EggIncId);
            Assert.AreEqual(1_700_000_500L, back.LastBackupTime);
            Assert.AreEqual("soul_eggs", back.EpicResearch.Single().Id);
            Assert.AreEqual((ushort)3, back.PermitLevel);
            Assert.AreEqual(5432.25, back.SoulEggs);
            Assert.AreEqual(6UL, back.NumPrestiges);
            Assert.AreEqual(Ei.Egg.Medical, back.MaxEggReached);
            Assert.AreEqual("device-proto-1", back.DeviceId);
            Assert.AreEqual(77.5, back.CraftingXP);
            Assert.IsTrue(back.HyperloopPurchased);

            back.EiBackupBytes = null;

            Assert.AreEqual(string.Empty, back.EggIncId);
            Assert.AreEqual(0L, back.LastBackupTime);
            Assert.IsNull(back.EpicResearch);
            Assert.AreEqual((ushort)0, back.PermitLevel);
            Assert.AreEqual((ushort)0, back.EggsOfProphecy);
            Assert.AreEqual(0d, back.SoulEggs);
            Assert.AreEqual(0d, back.CurrentMultiplier);
            Assert.AreEqual(0UL, back.NumPrestiges);
            Assert.AreEqual(0u, back.NumDailyGiftsCollected);
            Assert.IsNull(back.EggMedalLevel);
            Assert.AreEqual(0UL, back.GoldenEggsEarned);
            Assert.AreEqual(0UL, back.GoldenEggsSpent);
            Assert.AreEqual(0UL, back.PiggyBank);
            Assert.AreEqual(0UL, back.DroneTakedowns);
            Assert.AreEqual(0UL, back.DroneTakedownsElite);
            Assert.AreEqual(0UL, back.NumPiggyBreaks);
            Assert.IsFalse(back.HyperloopPurchased);
            Assert.AreEqual((byte)0, back.ClientVersion);
            Assert.AreEqual(default(Ei.Egg), back.MaxEggReached);
            Assert.IsNull(back.MaxFarmSizeReached);
            Assert.IsFalse(back.HasDeviceId);
            Assert.AreEqual(string.Empty, back.DeviceId);
            Assert.AreEqual(0d, back.CraftingXP);
            Assert.AreEqual(0, back.VirtueEggsDelivered.Length);
            Assert.AreEqual(0u, back.Resets);
            Assert.AreEqual(0u, back.ShiftCount);
            Assert.AreEqual(0, back.EovEarned.Length);
            Assert.IsFalse(back.NoAliasInLatestBackup);

            Assert.IsFalse(back.EmptyBackup);
            Assert.AreEqual("coop-derived-slot", back.ArchivedFarms.Single().CoopId);
            Assert.IsNotNull(back.CustomEggMaxFarmSizeReached);
        }

        private static CustomBackup BuildLegacyBackup() {
            return new CustomBackup {
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
        }

        private static void AssertLegacyValues(CustomBackup back) {
            Assert.IsNull(back.EiBackupBytes);
            Assert.AreEqual("EI-legacy-0001", back.EggIncId);
            Assert.AreEqual("LegacyName", back.UserName);
            Assert.AreEqual(1_650_000_000L, back.LastBackupTime);
            Assert.AreEqual("soul_eggs", back.EpicResearch.Single().Id);
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
        public void NonSuppression_LegacyValues_SurviveRoundTrip_WhenNoBlobPresent() {
            AssertLegacyValues(RoundTrip(BuildLegacyBackup(), StorageMessagePack.Options, StorageMessagePack.Options));
        }

        [TestMethod]
        public void UpgradePath_LegacyRowWrittenWithPlainOptions_ReadsIntactWithStorageOptions() {
            AssertLegacyValues(RoundTrip(BuildLegacyBackup(), Lz4, StorageMessagePack.Options));
        }

        [TestMethod]
        public void UserName_IsNotSuppressed_SoCarriedForwardAliasSurvives() {
            var (proto, contracts) = BuildBackup("");
            var lastBackup = new CustomBackup { UserName = "CarriedAlias" };

            var backup = new CustomBackup(proto, contracts, lastBackup);
            Assert.AreEqual("CarriedAlias", backup.UserName);
            Assert.IsTrue(backup.NoAliasInLatestBackup);

            var back = RoundTrip(backup, StorageMessagePack.Options, StorageMessagePack.Options);
            Assert.AreEqual("CarriedAlias", back.UserName);

            back.EiBackupBytes = null;
            Assert.AreEqual("CarriedAlias", back.UserName);
        }

        [TestMethod]
        public void NestedFarm_DerivedSlots_HoldDefaults_AfterFarmBlobsCleared() {
            var (proto, contracts) = BuildBackup();
            var back = RoundTrip(new CustomBackup(proto, contracts), StorageMessagePack.Options, StorageMessagePack.Options);
            var farm = back.Farms.Single();

            Assert.AreEqual(Ei.FarmType.Contract, farm.FarmType);
            Assert.AreEqual(ContractId, farm.ContractId);
            Assert.AreEqual((uint?)1, farm.League);
            Assert.AreEqual("coop-derived-slot", farm.CoopId);
            CollectionAssert.AreEqual(new List<uint> { 8u, 9u }, farm.Vehicles);

            farm.SimulationBytes = null;
            farm.LocalContractBytes = null;

            Assert.AreEqual(default(Ei.FarmType), farm.FarmType);
            Assert.IsNull(farm.ContractId);
            Assert.AreEqual(0d, farm.EggsPaidFor);
            Assert.IsNull(farm.CommonResearch);
            Assert.AreEqual(0UL, farm.NumChickens);
            Assert.AreEqual(default(Ei.Egg), farm.EggType);
            Assert.IsNull(farm.TrainLength);
            Assert.AreEqual(0u, farm.SilosOwned);
            Assert.AreEqual((ushort)0, farm.BoostTokensReceived);
            Assert.AreEqual((ushort)0, farm.BoostTokensGiven);
            Assert.AreEqual((ushort)0, farm.BoostTokensSpent);
            Assert.AreEqual(0d, farm.CashEarned);
            Assert.AreEqual(0d, farm.CashSpent);
            Assert.AreEqual(0L, farm.TimeCheatDebt);
            Assert.AreEqual((ushort)0, farm.TimeCheatsDetected);
            Assert.IsNull(farm.Habs);
            Assert.AreEqual(0f, farm.LastStepTime);

            Assert.IsNull(farm.League);
            Assert.IsNull(farm.CoopId);
            Assert.IsFalse(farm.Cancelled);
            Assert.AreEqual(0L, farm.TimeAccepted);
            Assert.AreEqual(0L, farm.CoopSharedEndTime);
            Assert.AreEqual((ushort)0, farm.BoostsUsed);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeUnset, farm.Grade);
            Assert.AreEqual(0d, farm.EvaluationCxp);
            Assert.IsFalse(farm.ContributionFinalized);
            Assert.AreEqual(0d, farm.CoopSimulationEndTime);
            Assert.AreEqual((byte)0, farm.NumGoalsAchieved);

            CollectionAssert.AreEqual(new List<uint> { 8u, 9u }, farm.Vehicles);
            Assert.IsNotNull(farm.Artifacts);
        }

        [TestMethod]
        public void Suppression_ShrinksSerializedPayload_ComparedToPlainOptions() {
            var (proto, contracts) = BuildBackup();
            var backup = new CustomBackup(proto, contracts);

            var suppressed = MessagePackSerializer.Serialize(backup, StorageMessagePack.Options);
            var plain = MessagePackSerializer.Serialize(backup, Lz4);

            Assert.IsTrue(suppressed.Length < plain.Length, $"suppressed {suppressed.Length} bytes was not smaller than plain {plain.Length} bytes");
        }

        [TestMethod]
        public void SuppressedPayload_StillReadableByPlainOptions() {
            var (proto, contracts) = BuildBackup();
            var source = new CustomBackup(proto, contracts);

            var back = RoundTrip(source, StorageMessagePack.Options, Lz4);

            CollectionAssert.AreEqual(source.EiBackupBytes, back.EiBackupBytes);
            Assert.AreEqual("EI0000000000012345", back.EggIncId);
            Assert.AreEqual("ProtoName", back.UserName);
            Assert.AreEqual(5432.25, back.SoulEggs);
            Assert.AreEqual(Ei.Egg.Medical, back.MaxEggReached);

            var farm = back.Farms.Single();
            Assert.AreEqual(ContractId, farm.ContractId);
            Assert.AreEqual(12345UL, farm.NumChickens);
            Assert.AreEqual((uint?)1, farm.League);
            CollectionAssert.AreEqual(new List<uint> { 8u, 9u }, farm.Vehicles);
        }
    }
}
