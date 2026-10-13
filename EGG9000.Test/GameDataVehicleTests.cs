using EGG9000.Common.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    public class GameDataVehicleTests {
        private static readonly Dictionary<uint, uint> Expected = new() {
            [0] = 5000,
            [1] = 15000,
            [2] = 50000,
            [3] = 100000,
            [4] = 250000,
            [5] = 500000,
            [6] = 1000000,
            [7] = 5000000,
            [8] = 15000000,
            [9] = 30000000,
            [10] = 50000000,
            [11] = 50000000,
        };

        [TestMethod]
        public void VehicleCapacitiesMatchEggSharp() {
            foreach(var (index, capacity) in Expected) {
                var entry = GameDataSource.Provider.Vehicles.Find((int)index);
                Assert.IsNotNull(entry, $"vehicle {index}");
                Assert.AreEqual((long)capacity, entry.Capacity, $"vehicle {index}");
            }
        }

        [TestMethod]
        public void ResearchVehiclesTableMatchesLegacyValues() {
            foreach(var (index, capacity) in Expected)
                Assert.AreEqual(capacity, Research.Vehicles[index], $"vehicle {index}");
        }
    }
}
