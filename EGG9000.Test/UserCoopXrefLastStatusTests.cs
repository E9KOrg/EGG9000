using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class UserCoopXrefLastStatusTests {

        [TestMethod]
        public void LegacyJsonStatus_MigratesToCompact_WithoutByteWrite() {
            var info = new Ei.ContractCoopStatusResponse.Types.ContributionInfo { SoulPower = 12.5, ContributionAmount = 42, BoostTokensSpent = 3 };
            var xref = new UserCoopXref { Status = JsonConvert.SerializeObject(info) };

            var migrated = xref.LastStatus;

            Assert.AreEqual(12.5, migrated.SoulPower);
            Assert.AreEqual(42d, migrated.ContributionAmount);
            Assert.AreEqual(3u, migrated.BoostTokensSpent);
            Assert.IsNull(xref.Status);
            Assert.IsNull(xref._lastStatusByte);
        }

        [TestMethod]
        public void Setter_WritesByteColumn_AndRoundTrips() {
            var xref = new UserCoopXref {
                LastStatus = new ContributionInfoCompact { UserName = "Tester", SoulPower = 5 }
            };

            Assert.IsNotNull(xref._lastStatusByte);
            var reloaded = new UserCoopXref { _lastStatusByte = xref._lastStatusByte };
            Assert.AreEqual("Tester", reloaded.LastStatus.UserName);
            Assert.AreEqual(5d, reloaded.LastStatus.SoulPower);
        }

        [TestMethod]
        public void NullStatusString_SkipsMigration() {
            var xref = new UserCoopXref { Status = "null" };

            Assert.IsNull(xref.LastStatus);
            Assert.AreEqual("null", xref.Status);
        }
    }
}
