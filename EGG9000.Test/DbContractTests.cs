using EGG9000.Common.Database.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class DbContractTests {

        private static Ei.Contract FullProto() {
            return new Ei.Contract {
                Identifier = "test-contract",
                Name = "Test Contract",
                ExpirationTime = 1_700_000_000,
                MaxCoopSize = 10,
                Egg = Ei.Egg.Tachyon,
                CcOnly = true,
                SeasonId = "winter_2026",
                LengthSeconds = 3600
            };
        }

        [TestMethod]
        public void ContractTime_PrefersDetailsBlob() {
            var contract = new DBContract { length_seconds = 999, P7 = 5 };
            contract.ApplyDetails(new Ei.Contract { Identifier = "x", LengthSeconds = 3600 });

            Assert.AreEqual(TimeSpan.FromHours(1), contract.ContractTime);
        }

        [TestMethod]
        public void ContractTime_FallsBackToLengthSeconds_WhenBlobAbsent() {
            var contract = new DBContract { _response = null, length_seconds = 1200, P7 = 5 };

            Assert.AreEqual(TimeSpan.FromMinutes(20), contract.ContractTime);
        }

        [TestMethod]
        public void ContractTime_FallsBackToP7_Last() {
            var contract = new DBContract { _response = null, length_seconds = 0, P7 = 7200 };

            Assert.AreEqual(TimeSpan.FromHours(2), contract.ContractTime);
        }

        [TestMethod]
        public void ContractTime_BlobZeroLength_FallsThrough() {
            var contract = new DBContract { length_seconds = 1200 };
            contract.ApplyDetails(new Ei.Contract { Identifier = "x", LengthSeconds = 0 });

            Assert.AreEqual(TimeSpan.FromMinutes(20), contract.ContractTime);
        }

        [TestMethod]
        public void ApplyDetails_MapsEverySyncedColumn() {
            var proto = FullProto();
            var contract = new DBContract { ID = proto.Identifier };

            contract.ApplyDetails(proto);

            Assert.AreEqual("Test Contract", contract.Name);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), contract.GoodUntil);
            Assert.AreEqual(10, contract.MaxUsers);
            Assert.AreEqual(Ei.Egg.Tachyon.ToString(), contract.egg);
            Assert.IsTrue(contract.cc_only);
            Assert.AreEqual("winter_2026", contract.SeasonId);
            Assert.IsNotNull(contract._response);
        }

        [TestMethod]
        public void Serialization_IsStableForChangeDetection() {
            var first = JsonConvert.SerializeObject(FullProto());
            var second = JsonConvert.SerializeObject(FullProto());
            Assert.AreEqual(first, second);

            var contract = new DBContract();
            contract.ApplyDetails(FullProto());
            Assert.AreEqual(JsonConvert.SerializeObject(FullProto()), contract._response);
        }

        [TestMethod]
        public void Details_NullSafety_OnFreshContract() {
            var contract = new DBContract { length_seconds = 600, P7 = 30 };

            Assert.IsNull(contract.Details);
            Assert.IsNull(contract.SeasonId);
            Assert.AreEqual(TimeSpan.FromMinutes(10), contract.ContractTime);
        }
    }
}
