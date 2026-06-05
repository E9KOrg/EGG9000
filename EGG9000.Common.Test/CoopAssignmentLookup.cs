using EGG9000.Common.Database;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Test {
    [TestClass]
    public class CoopAssignmentLookupTest {
        private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
        private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Coop1 = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid Coop2 = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        [TestMethod]
        public void Build_GroupsMultipleCoopsUnderOneUserContractKey() {
            var rows = new List<CoopAssignmentRow> {
                new(UserA, Coop1, "c1", 100, 0, "alpha"),
                new(UserA, Coop2, "c1", 200, 0, "beta"),
            };

            var map = CoopAssignmentLookup.Build(rows);

            Assert.AreEqual(1, map.Count);
            Assert.AreEqual(2, map[(UserA, "c1")].Count);
        }

        [TestMethod]
        public void Build_DeduplicatesSameCoopId() {
            var rows = new List<CoopAssignmentRow> {
                new(UserA, Coop1, "c1", 100, 0, "alpha"),
                new(UserA, Coop1, "c1", 100, 0, "alpha"),
            };

            var map = CoopAssignmentLookup.Build(rows);

            Assert.AreEqual(1, map[(UserA, "c1")].Count);
        }

        [TestMethod]
        public void Build_SeparatesByUserAndContract() {
            var rows = new List<CoopAssignmentRow> {
                new(UserA, Coop1, "c1", 100, 0, "alpha"),
                new(UserB, Coop1, "c1", 100, 0, "alpha"),
                new(UserA, Coop2, "c2", 200, 0, "beta"),
            };

            var map = CoopAssignmentLookup.Build(rows);

            Assert.AreEqual(3, map.Count);
            Assert.IsTrue(map.ContainsKey((UserA, "c1")));
            Assert.IsTrue(map.ContainsKey((UserB, "c1")));
            Assert.IsTrue(map.ContainsKey((UserA, "c2")));
        }

        [TestMethod]
        public void Build_PreservesCoopFields() {
            var rows = new List<CoopAssignmentRow> { new(UserA, Coop1, "c1", 555, 777, "gamma") };

            var coop = CoopAssignmentLookup.Build(rows)[(UserA, "c1")].Single();

            Assert.AreEqual(Coop1, coop.CoopId);
            Assert.AreEqual(555ul, coop.ThreadId);
            Assert.AreEqual(777ul, coop.DiscordChannelId);
            Assert.AreEqual("gamma", coop.Name);
            Assert.AreEqual("c1", coop.ContractId);
        }

        [TestMethod]
        public void Build_EmptyInputProducesEmptyMap() {
            Assert.AreEqual(0, CoopAssignmentLookup.Build([]).Count);
        }
    }
}
