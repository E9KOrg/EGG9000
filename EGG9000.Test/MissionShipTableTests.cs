using EGG9000.Common.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using static Ei.MissionInfo.Types;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class MissionShipTableTests {

        private static readonly Dictionary<Spaceship, int[]> Requirements = new() {
            [Spaceship.ChickenOne] = [],
            [Spaceship.ChickenNine] = [4, 10],
            [Spaceship.ChickenHeavy] = [5, 15, 25],
            [Spaceship.Bcr] = [5, 15, 25, 40],
            [Spaceship.MilleniumChicken] = [10, 25, 40, 50],
            [Spaceship.CorellihenCorvette] = [10, 25, 40, 50],
            [Spaceship.Galeggtica] = [10, 25, 40, 50, 60],
            [Spaceship.Chickfiant] = [10, 25, 40, 50, 60],
            [Spaceship.Voyegger] = [10, 25, 40, 50, 60, 70],
            [Spaceship.Henerprise] = [10, 25, 40, 50, 60, 70, 80, 100],
            [Spaceship.Atreggies] = [20, 40, 80, 120, 160, 200, 300, 500],
        };

        private static readonly (Spaceship Ship, DurationType Duration, int Seconds, int Capacity, int Bump)[] Durations = [
            (Spaceship.ChickenOne, DurationType.Tutorial, 60, 4, 0),
            (Spaceship.ChickenOne, DurationType.Short, 1200, 4, 0),
            (Spaceship.ChickenOne, DurationType.Long, 3600, 5, 1),
            (Spaceship.ChickenOne, DurationType.Epic, 7200, 6, 1),
            (Spaceship.ChickenNine, DurationType.Short, 1800, 7, 1),
            (Spaceship.ChickenNine, DurationType.Long, 3600, 8, 1),
            (Spaceship.ChickenNine, DurationType.Epic, 10800, 9, 1),
            (Spaceship.ChickenHeavy, DurationType.Short, 2700, 12, 1),
            (Spaceship.ChickenHeavy, DurationType.Long, 5400, 14, 2),
            (Spaceship.ChickenHeavy, DurationType.Epic, 14400, 15, 2),
            (Spaceship.Bcr, DurationType.Short, 5400, 18, 2),
            (Spaceship.Bcr, DurationType.Long, 14400, 20, 2),
            (Spaceship.Bcr, DurationType.Epic, 28800, 22, 3),
            (Spaceship.MilleniumChicken, DurationType.Short, 10800, 10, 1),
            (Spaceship.MilleniumChicken, DurationType.Long, 21600, 12, 2),
            (Spaceship.MilleniumChicken, DurationType.Epic, 43200, 14, 2),
            (Spaceship.CorellihenCorvette, DurationType.Short, 14400, 18, 3),
            (Spaceship.CorellihenCorvette, DurationType.Long, 43200, 21, 3),
            (Spaceship.CorellihenCorvette, DurationType.Epic, 86400, 24, 4),
            (Spaceship.Galeggtica, DurationType.Short, 21600, 27, 4),
            (Spaceship.Galeggtica, DurationType.Long, 57600, 30, 5),
            (Spaceship.Galeggtica, DurationType.Epic, 108000, 35, 6),
            (Spaceship.Chickfiant, DurationType.Short, 28800, 20, 3),
            (Spaceship.Chickfiant, DurationType.Long, 86400, 24, 4),
            (Spaceship.Chickfiant, DurationType.Epic, 172800, 28, 5),
            (Spaceship.Voyegger, DurationType.Short, 43200, 30, 4),
            (Spaceship.Voyegger, DurationType.Long, 129600, 35, 5),
            (Spaceship.Voyegger, DurationType.Epic, 259200, 40, 6),
            (Spaceship.Henerprise, DurationType.Short, 86400, 45, 5),
            (Spaceship.Henerprise, DurationType.Long, 172800, 50, 6),
            (Spaceship.Henerprise, DurationType.Epic, 345600, 56, 7),
            (Spaceship.Atreggies, DurationType.Short, 172800, 60, 7),
            (Spaceship.Atreggies, DurationType.Long, 259200, 78, 8),
            (Spaceship.Atreggies, DurationType.Epic, 345600, 86, 10),
        ];

        [TestMethod]
        public void Every_ship_is_present() {
            CollectionAssert.AreEquivalent(Requirements.Keys.ToList(), MissionHelpers.MaxShipLevels.Keys.ToList());
        }

        [TestMethod]
        public void Max_ship_levels_match() {
            foreach(var (ship, reqs) in Requirements)
                Assert.AreEqual((uint)reqs.Length, MissionHelpers.MaxShipLevels[ship], ship.ToString());
        }

        [TestMethod]
        public void Level_requirements_match() {
            foreach(var (ship, reqs) in Requirements)
                CollectionAssert.AreEqual(reqs, MissionHelpers.LevelRequirements[ship], ship.ToString());
        }

        [TestMethod]
        public void Durations_per_ship_match() {
            foreach(var ship in Requirements.Keys) {
                var expected = Durations.Where(d => d.Ship == ship).Select(d => d.Duration).ToList();
                CollectionAssert.AreEquivalent(expected, MissionHelpers.NominalShipCapacities[ship].Keys.ToList(), ship.ToString());
                CollectionAssert.AreEquivalent(expected, MissionHelpers.ShipBaseTimesMinutes[ship].Keys.ToList(), ship.ToString());
            }
        }

        [TestMethod]
        public void Base_minutes_match() {
            foreach(var d in Durations)
                Assert.AreEqual(d.Seconds / 60, MissionHelpers.ShipBaseTimesMinutes[d.Ship][d.Duration], $"{d.Ship}/{d.Duration}");
        }

        [TestMethod]
        public void Nominal_capacities_match() {
            foreach(var d in Durations) {
                var levels = Requirements[d.Ship].Length;
                var expected = Enumerable.Range(0, levels + 1).Select(i => d.Capacity + i * d.Bump).ToList();
                CollectionAssert.AreEqual(expected, MissionHelpers.NominalShipCapacities[d.Ship][d.Duration], $"{d.Ship}/{d.Duration}");
            }
        }

        [TestMethod]
        public void Spot_check_capacity_literals() {
            CollectionAssert.AreEqual(new List<int> { 60, 67, 74, 81, 88, 95, 102, 109, 116 }, MissionHelpers.NominalShipCapacities[Spaceship.Atreggies][DurationType.Short]);
            CollectionAssert.AreEqual(new List<int> { 86, 96, 106, 116, 126, 136, 146, 156, 166 }, MissionHelpers.NominalShipCapacities[Spaceship.Atreggies][DurationType.Epic]);
            CollectionAssert.AreEqual(new List<int> { 4 }, MissionHelpers.NominalShipCapacities[Spaceship.ChickenOne][DurationType.Tutorial]);
        }
    }
}
