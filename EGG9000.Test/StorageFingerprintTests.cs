using EGG9000.Common.Database;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.RegularExpressions;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageFingerprintTests {
        private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$");

        [TestMethod]
        public void Accounts_IsStableSha256Hex() {
            var first = StorageFingerprint.Accounts();
            Assert.IsTrue(Sha256Hex.IsMatch(first), first);
            Assert.AreEqual(first, StorageFingerprint.Accounts());
        }

        [TestMethod]
        public void CoopStatus_IsStableSha256Hex() {
            var first = StorageFingerprint.CoopStatus();
            Assert.IsTrue(Sha256Hex.IsMatch(first), first);
            Assert.AreEqual(first, StorageFingerprint.CoopStatus());
        }

        [TestMethod]
        public void Accounts_And_CoopStatus_Differ() {
            Assert.AreNotEqual(StorageFingerprint.Accounts(), StorageFingerprint.CoopStatus());
        }

        [TestMethod]
        public void AccountsDescription_CoversGraphTrimListAndStrategy() {
            var description = StorageFingerprint.AccountsDescription();
            StringAssert.Contains(description, "type EGG9000.Common.Database.Entities.EggIncAccount");
            StringAssert.Contains(description, "type EGG9000.Common.Database.CustomBackup");
            StringAssert.Contains(description, "member EGG9000.Common.Database.Entities.EggIncAccount 10 Backup EGG9000.Common.Database.CustomBackup");
            StringAssert.Contains(description, "notstored Ei.Backup:");
            StringAssert.Contains(description, "descriptor ");
            StringAssert.Contains(description, "strategy Zstd zstd=9 raw=64");
        }
    }
}
