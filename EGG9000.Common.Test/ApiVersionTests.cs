using EGG9000.Common.EggIncAPI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test {
    [TestClass]
    public class ApiVersionTests {

        [TestMethod]
        public void IsValidPeriodicalsResponse_NullResponse_False() {
            Assert.IsFalse(EggIncApi.IsValidPeriodicalsResponse(null));
        }

        [TestMethod]
        public void IsValidPeriodicalsResponse_NoContracts_False() {
            var resp = new Ei.PeriodicalsResponse { Contracts = new Ei.ContractsResponse() };
            Assert.IsFalse(EggIncApi.IsValidPeriodicalsResponse(resp));
        }

        [TestMethod]
        public void IsValidPeriodicalsResponse_HasContract_True() {
            var resp = new Ei.PeriodicalsResponse { Contracts = new Ei.ContractsResponse() };
            resp.Contracts.Contracts.Add(new Ei.Contract { Identifier = "space-stock-2026" });
            Assert.IsTrue(EggIncApi.IsValidPeriodicalsResponse(resp));
        }

        [TestMethod]
        public void SetVersions_UpdatesAllThree() {
            var oldClient = EggIncApi.ClientVersion;
            var oldVersion = EggIncApi.AppVersion;
            var oldBuild = EggIncApi.AppBuild;
            try {
                EggIncApi.SetVersions(999, "9.9.9", "9.9.9.9");
                Assert.AreEqual(999u, EggIncApi.ClientVersion);
                Assert.AreEqual("9.9.9", EggIncApi.AppVersion);
                Assert.AreEqual("9.9.9.9", EggIncApi.AppBuild);
            } finally {
                EggIncApi.SetVersions(oldClient, oldVersion, oldBuild);
            }
        }
    }
}
