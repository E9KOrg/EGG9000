using EGG9000.Common.Database;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class MessagePackKeyMapTests {
        private static readonly Dictionary<int, string> CustomBackupKeys = new() {
            [0] = "Farms",
            [1] = "EggIncId",
            [2] = "UserName",
            [4] = "LastBackupTime",
            [5] = "EpicResearch",
            [6] = "PermitLevel",
            [7] = "CacheAdded",
            [8] = "EggsOfProphecy",
            [9] = "SoulEggs",
            [10] = "CurrentMultiplier",
            [12] = "EmptyBackup",
            [13] = "ArchivedFarms",
            [14] = "NumPrestiges",
            [15] = "SpaceMissions",
            [16] = "NumDailyGiftsCollected",
            [17] = "EggMedalLevel",
            [18] = "GoldenEggsEarned",
            [19] = "GoldenEggsSpent",
            [20] = "PiggyBank",
            [21] = "DroneTakedowns",
            [22] = "DroneTakedownsElite",
            [23] = "NumPiggyBreaks",
            [24] = "ArtifactHall",
            [25] = "HyperloopPurchased",
            [26] = "TankLevel",
            [28] = "ClientVersion",
            [29] = "FuelAmounts",
            [31] = "MaxEggReached",
            [32] = "MaxFarmSizeReached",
            [33] = "HasDeviceId",
            [34] = "DeviceId",
            [36] = "ShipsSent",
            [37] = "SeasonCS",
            [38] = "TotalCS",
            [39] = "ArtifactSets",
            [40] = "CraftingXP",
            [41] = "FuelingMission",
            [42] = "CustomEggMaxFarmSizeReached",
            [44] = "VirtueEggsDelivered",
            [45] = "Resets",
            [46] = "ShiftCount",
            [47] = "EovEarned",
            [48] = "SubscriptionEnds",
            [49] = "SubscriptionLevel",
            [50] = "NoAliasInLatestBackup",
            [51] = "LastContractPlayerInfoBytes",
            [52] = "EiBackupBytes"
        };

        private static readonly Dictionary<int, string> CustomFarmKeys = new() {
            [0] = "FarmType",
            [1] = "ContractId",
            [2] = "EggsPaidFor",
            [3] = "League",
            [4] = "CoopId",
            [5] = "Cancelled",
            [6] = "Completed",
            [7] = "CommonResearch",
            [8] = "NumChickens",
            [10] = "EggType",
            [11] = "TrainLength",
            [12] = "Vehicles",
            [13] = "Artifacts",
            [14] = "SilosOwned",
            [15] = "TimeAccepted",
            [16] = "CoopAllowed",
            [17] = "CoopSharedEndTime",
            [18] = "BoostTokensReceived",
            [19] = "BoostTokensGiven",
            [20] = "BoostTokensSpent",
            [21] = "CashEarned",
            [22] = "CashSpent",
            [23] = "TimeCheatDebt",
            [24] = "BoostsUsed",
            [25] = "TimeCheatsDetected",
            [32] = "Habs",
            [33] = "LastStepTime",
            [34] = "ReportedUUIDs",
            [35] = "Grade",
            [36] = "EvaluationCxp",
            [37] = "ContributionFinalized",
            [38] = "CoopSimulationEndTime",
            [39] = "NumGoalsAchieved",
            [40] = "Creator",
            [41] = "SimulationBytes",
            [42] = "LocalContractBytes"
        };

        private static readonly Dictionary<int, string> CustomArchivedFarmsKeys = new() {
            [0] = "CoopId",
            [1] = "ContractId",
            [2] = "TimeAccepted",
            [3] = "Completed",
            [4] = "League",
            [5] = "PEPossible",
            [6] = "PEGained",
            [7] = "ContributionAmount",
            [8] = "Grade",
            [9] = "EvaluationCxp",
            [10] = "NumGoalsAchieved",
            [11] = "ReportedUUIDs"
        };

        private static readonly Dictionary<int, string> CustomResearchKeys = new() {
            [0] = "Id",
            [1] = "Level"
        };

        private static readonly Dictionary<int, string> SpaceMissionKeys = new() {
            [0] = "Ship",
            [1] = "Duration",
            [2] = "Status",
            [3] = "Fuels",
            [4] = "DurationSeconds",
            [5] = "StartTime",
            [6] = "Targeting",
            [7] = "Capacity",
            [8] = "Stars"
        };

        private static readonly Dictionary<int, string> SpaceMissionFuelKeys = new() {
            [0] = "Egg",
            [1] = "Amount"
        };

        private static readonly int[] CustomBackupRetiredKeys = [3, 11, 27, 30, 35, 43];

        private static readonly int[] CustomFarmRetiredKeys = [9, 26, 27, 28, 29, 30, 31];

        private static List<(int Key, string Member)> KeyedMembers(Type type) {
            var members = new List<(int Key, string Member)>();
            foreach(var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if(property.GetCustomAttribute<KeyAttribute>()?.IntKey is { } key)
                    members.Add((key, property.Name));
            }
            foreach(var fieldMember in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                if(fieldMember.GetCustomAttribute<KeyAttribute>()?.IntKey is { } key)
                    members.Add((key, fieldMember.Name));
            }
            return members;
        }

        private static Dictionary<int, string> KeyMap(Type type) {
            return KeyedMembers(type).ToDictionary(x => x.Key, x => x.Member);
        }

        private static void AssertKeyMap(Type type, Dictionary<int, string> expected) {
            var actual = KeyMap(type);
            foreach(var pair in expected) {
                Assert.IsTrue(actual.TryGetValue(pair.Key, out var name), $"{type.Name} lost [Key({pair.Key})] {pair.Value}");
                Assert.AreEqual(pair.Value, name, $"{type.Name} [Key({pair.Key})]");
            }
            var unpinned = actual.Keys.Except(expected.Keys).OrderBy(x => x).ToList();
            Assert.AreEqual(0, unpinned.Count, $"{type.Name} has keys not in the pinned map: {string.Join(", ", unpinned)}");
        }

        private static void AssertRetiredKeysAbsent(Type type, int[] retired) {
            var actual = KeyMap(type);
            foreach(var key in retired)
                Assert.IsFalse(actual.ContainsKey(key), $"{type.Name} reuses retired [Key({key})] on {actual.GetValueOrDefault(key)}");
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly) {
            try {
                return assembly.GetTypes();
            } catch(ReflectionTypeLoadException e) {
                return e.Types.Where(t => t is not null).Select(t => t!);
            }
        }

        [TestMethod]
        public void CustomBackup_KeyMap_MatchesPinnedLayout() {
            AssertKeyMap(typeof(CustomBackup), CustomBackupKeys);
        }

        [TestMethod]
        public void CustomFarm_KeyMap_MatchesPinnedLayout() {
            AssertKeyMap(typeof(CustomFarm), CustomFarmKeys);
        }

        [TestMethod]
        public void CustomArchivedFarms_KeyMap_MatchesPinnedLayout() {
            AssertKeyMap(typeof(CustomArchivedFarms), CustomArchivedFarmsKeys);
        }

        [TestMethod]
        public void CustomResearch_KeyMap_MatchesPinnedLayout() {
            AssertKeyMap(typeof(CustomResearch), CustomResearchKeys);
        }

        [TestMethod]
        public void SpaceMission_KeyMap_MatchesPinnedLayout() {
            AssertKeyMap(typeof(SpaceMission), SpaceMissionKeys);
        }

        [TestMethod]
        public void SpaceMissionFuel_KeyMap_MatchesPinnedLayout() {
            AssertKeyMap(typeof(SpaceMissionFuel), SpaceMissionFuelKeys);
        }

        [TestMethod]
        public void CustomBackup_RetiredKeys_AreAbsent() {
            AssertRetiredKeysAbsent(typeof(CustomBackup), CustomBackupRetiredKeys);
        }

        [TestMethod]
        public void CustomFarm_RetiredKeys_AreAbsent() {
            AssertRetiredKeysAbsent(typeof(CustomFarm), CustomFarmRetiredKeys);
        }

        [TestMethod]
        public void DatabaseMessagePackTypes_HaveNoDuplicateIntKeys() {
            var types = LoadableTypes(typeof(CustomBackup).Assembly)
                .Where(t => t.Namespace?.StartsWith("EGG9000.Common.Database", StringComparison.Ordinal) == true)
                .Where(t => t.GetCustomAttribute<MessagePackObjectAttribute>() is not null)
                .ToList();

            CollectionAssert.Contains(types, typeof(CustomBackup));
            CollectionAssert.Contains(types, typeof(CustomFarm));
            CollectionAssert.Contains(types, typeof(CustomArchivedFarms));

            foreach(var type in types) {
                var duplicates = KeyedMembers(type)
                    .GroupBy(x => x.Key)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key} ({string.Join(", ", g.Select(x => x.Member))})")
                    .ToList();
                Assert.AreEqual(0, duplicates.Count, $"{type.FullName} has duplicate int keys: {string.Join("; ", duplicates)}");
            }
        }
    }
}
