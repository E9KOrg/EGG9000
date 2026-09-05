using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Proto;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageRoundTripTests {
        private const string ContractId = "contract-storage-roundtrip";

        private static (Ei.Backup Backup, FrozenSet<Ei.Contract> Contracts) BuildBackup() {
            var contract = new Ei.Contract { Identifier = ContractId };
            var localContract = new Ei.LocalContract {
                Contract = contract,
                ContractIdentifier = ContractId,
                CoopIdentifier = "coop-storage-roundtrip",
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

        [TestMethod]
        public void DBUser_BlobBackedAccount_SurvivesProductionStoragePath() {
            var (proto, contracts) = BuildBackup();
            var user = new DBUser();
            user.EggIncAccounts = [new EggIncAccount { Id = "EI0000000000012345", Backup = new CustomBackup(proto, contracts) }];

            Assert.IsNotNull(user._contractRegistrationByte);

            var rehydrated = new DBUser { _contractRegistrationByte = user._contractRegistrationByte };
            var accounts = rehydrated.EggIncAccounts;

            Assert.AreEqual(1, accounts.Count);
            var account = accounts.Single();
            Assert.AreEqual("EI0000000000012345", account.Id);
            Assert.IsNotNull(account.Backup);
            Assert.IsNotNull(account.Backup.EiBackupBytes);
            Assert.AreEqual("EI0000000000012345", account.Backup.EggIncId);
            Assert.AreEqual("ProtoName", account.Backup.UserName);
            Assert.AreEqual(1_700_000_500L, account.Backup.LastBackupTime);
            Assert.AreEqual(5432.25, account.Backup.SoulEggs);
            Assert.AreEqual((ushort)3, account.Backup.PermitLevel);
            Assert.AreEqual(6UL, account.Backup.NumPrestiges);
            Assert.AreEqual(Ei.Egg.Medical, account.Backup.MaxEggReached);
            Assert.IsTrue(account.Backup.HyperloopPurchased);
            Assert.AreEqual(77.5, account.Backup.CraftingXP);

            var farm = account.Backup.Farms.Single();
            Assert.AreEqual(ContractId, farm.ContractId);
            Assert.AreEqual(12345UL, farm.NumChickens);
            Assert.AreEqual("coop-storage-roundtrip", farm.CoopId);
            Assert.AreEqual((uint?)1, farm.League);

            Assert.AreEqual("device-proto-1", account.DeviceID);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeAa, account.LastGrade);
        }

        [TestMethod]
        public void DBUser_CorruptStorageBytes_YieldEmptyAccountsWithoutThrowing() {
            var user = new DBUser { _contractRegistrationByte = [0xC1, 0xFF, 0x00] };

            var accounts = user.EggIncAccounts;

            Assert.IsNotNull(accounts);
            Assert.AreEqual(0, accounts.Count);
            Assert.IsTrue(user.AccountsUnreadable);
            Assert.IsFalse(user.UpdateAccounts());
            CollectionAssert.AreEqual(new byte[] { 0xC1, 0xFF, 0x00 }, user._contractRegistrationByte);
        }

        [TestMethod]
        public void UpdateAccounts_ReportsChangedOnlyWhenBytesDiffer() {
            var user = new DBUser { DiscordId = 9 };
            user.EggIncAccounts = [new EggIncAccount { Id = "EI0000000000000009", Guild = "before" }];
            var stored = user._contractRegistrationByte;

            Assert.IsFalse(user.UpdateAccounts());
            Assert.AreSame(stored, user._contractRegistrationByte);

            user.EggIncAccounts[0].Guild = "after";
            Assert.IsTrue(user.UpdateAccounts());
            Assert.AreNotSame(stored, user._contractRegistrationByte);
            Assert.AreEqual("after", new DBUser { _contractRegistrationByte = user._contractRegistrationByte }.EggIncAccounts[0].Guild);
        }

        [TestMethod]
        public void SoloFarm_SimulationGateSuppresses_WhileLegacyLocalContractValuesSurvive() {
            var simulation = new Ei.Backup.Types.Simulation {
                FarmType = Ei.FarmType.Home,
                EggsPaidFor = 42.5,
                NumChickens = 4242,
                EggType = Ei.Egg.Tachyon,
                SilosOwned = 3
            };
            var farm = new CustomFarm {
                SimulationBytes = StorageTrimmer.TrimmedBytes(simulation),
                LocalContractBytes = null,
                League = 2,
                CoopId = "legacy-coop",
                TimeAccepted = 1_600_000_000,
                Grade = Ei.Contract.Types.PlayerGrade.GradeA,
                BoostsUsed = 4
            };
            var backup = new CustomBackup { Farms = [farm] };

            var bytes = MessagePackSerializer.Serialize(backup, StorageMessagePack.Options);
            var back = MessagePackSerializer.Deserialize<CustomBackup>(bytes, StorageMessagePack.Options);
            var soloFarm = back.Farms.Single();

            Assert.IsNotNull(soloFarm.SimulationBytes);
            Assert.IsNull(soloFarm.LocalContractBytes);
            Assert.AreEqual(Ei.FarmType.Home, soloFarm.FarmType);
            Assert.AreEqual(42.5, soloFarm.EggsPaidFor);
            Assert.AreEqual(4242UL, soloFarm.NumChickens);
            Assert.AreEqual(Ei.Egg.Tachyon, soloFarm.EggType);
            Assert.AreEqual(3u, soloFarm.SilosOwned);
            Assert.AreEqual((uint?)2, soloFarm.League);
            Assert.AreEqual("legacy-coop", soloFarm.CoopId);
            Assert.AreEqual(1_600_000_000L, soloFarm.TimeAccepted);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeA, soloFarm.Grade);
            Assert.AreEqual((ushort)4, soloFarm.BoostsUsed);

            soloFarm.SimulationBytes = null;

            Assert.AreEqual(default(Ei.FarmType), soloFarm.FarmType);
            Assert.AreEqual(0d, soloFarm.EggsPaidFor);
            Assert.AreEqual(0UL, soloFarm.NumChickens);
            Assert.AreEqual(default(Ei.Egg), soloFarm.EggType);
            Assert.AreEqual(0u, soloFarm.SilosOwned);
            Assert.AreEqual((uint?)2, soloFarm.League);
            Assert.AreEqual("legacy-coop", soloFarm.CoopId);
            Assert.AreEqual(1_600_000_000L, soloFarm.TimeAccepted);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeA, soloFarm.Grade);
            Assert.AreEqual((ushort)4, soloFarm.BoostsUsed);
        }

        [TestMethod]
        public void MixedList_LegacyAndBlobBacked_SurviveTwoRoundTrips() {
            var (proto, contracts) = BuildBackup();
            var list = new List<CustomBackup> { BuildLegacyBackup(), new CustomBackup(proto, contracts) };

            var first = MessagePackSerializer.Deserialize<List<CustomBackup>>(
                MessagePackSerializer.Serialize(list, StorageMessagePack.Options), StorageMessagePack.Options);
            AssertMixedListShape(first);

            var second = MessagePackSerializer.Deserialize<List<CustomBackup>>(
                MessagePackSerializer.Serialize(first, StorageMessagePack.Options), StorageMessagePack.Options);
            AssertMixedListShape(second);
        }

        private static void AssertMixedListShape(List<CustomBackup> list) {
            Assert.AreEqual(2, list.Count);

            var legacy = list[0];
            Assert.IsNull(legacy.EiBackupBytes);
            Assert.AreEqual("EI-legacy-0001", legacy.EggIncId);
            Assert.AreEqual("LegacyName", legacy.UserName);
            Assert.AreEqual(1_650_000_000L, legacy.LastBackupTime);
            Assert.AreEqual("soul_eggs", legacy.EpicResearch.Single().Id);
            Assert.AreEqual((ushort)2, legacy.PermitLevel);
            Assert.AreEqual(999.5, legacy.SoulEggs);
            Assert.AreEqual(3UL, legacy.NumPrestiges);
            Assert.IsTrue(legacy.HyperloopPurchased);
            Assert.AreEqual(Ei.Egg.Tachyon, legacy.MaxEggReached);
            Assert.AreEqual(8u, legacy.Resets);

            var derived = list[1];
            Assert.IsNotNull(derived.EiBackupBytes);
            Assert.AreEqual("EI0000000000012345", derived.EggIncId);
            Assert.AreEqual("ProtoName", derived.UserName);
            Assert.AreEqual(1_700_000_500L, derived.LastBackupTime);
            Assert.AreEqual("soul_eggs", derived.EpicResearch.Single().Id);
            Assert.AreEqual((ushort)3, derived.PermitLevel);
            Assert.AreEqual(5432.25, derived.SoulEggs);
            Assert.AreEqual(6UL, derived.NumPrestiges);
            Assert.IsTrue(derived.HyperloopPurchased);
            Assert.AreEqual(Ei.Egg.Medical, derived.MaxEggReached);
        }

        [TestMethod]
        public void EveryDerivedSlotType_ResolvesToDerivedSlotFormatter_InStorageOptions() {
            var derivedSlotTypes = LoadableTypes(typeof(CustomBackup).Assembly)
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Any(p => p.GetCustomAttribute<DerivedSlotAttribute>() is not null))
                .ToList();

            CollectionAssert.Contains(derivedSlotTypes, typeof(CustomBackup));
            CollectionAssert.Contains(derivedSlotTypes, typeof(CustomFarm));

            var getFormatter = typeof(FormatterResolverExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "GetFormatterWithVerify" && m.IsGenericMethodDefinition);

            foreach(var type in derivedSlotTypes) {
                var formatter = getFormatter.MakeGenericMethod(type).Invoke(null, [StorageMessagePack.Options.Resolver]);
                Assert.IsNotNull(formatter, type.FullName);
                var formatterType = formatter.GetType();
                Assert.IsTrue(formatterType.IsGenericType && formatterType.GetGenericTypeDefinition() == typeof(DerivedSlotFormatter<>),
                    $"{type.FullName} resolved {formatterType.FullName} instead of DerivedSlotFormatter");
                Assert.AreEqual(type, formatterType.GetGenericArguments()[0], type.FullName);
            }
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly) {
            try {
                return assembly.GetTypes();
            } catch(ReflectionTypeLoadException e) {
                return e.Types.Where(t => t is not null).Select(t => t!);
            }
        }
    }
}
