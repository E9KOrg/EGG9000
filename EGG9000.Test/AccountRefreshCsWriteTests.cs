using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Google.Protobuf;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class AccountRefreshCsWriteTests {
        private static (DBUser user, EggIncAccount account) UserWithBackup() {
            var account = new EggIncAccount {
                Id = "EI0000000000000004",
                LastGrade = Ei.Contract.Types.PlayerGrade.GradeAaa,
                Backup = new CustomBackup { TotalCS = 100, SeasonCS = 50, LastContractPlayerInfoBytes = [1, 2, 3] }
            };
            var user = new DBUser { DiscordUsername = "test" };
            user.EggIncAccounts = [account];
            return (user, account);
        }

        [TestMethod]
        public void Complete_WritesCsAndTrimmedBytes() {
            var (user, account) = UserWithBackup();
            var info = new Ei.ContractPlayerInfo {
                Grade = Ei.Contract.Types.PlayerGrade.GradeAaa,
                Status = Ei.ContractPlayerInfo.Types.Status.Complete,
                TotalCxp = 200,
                SeasonCxp = 75
            };
            info.UnreadEvaluations.Add(new Ei.ContractEvaluation());
            info.SeasonProgress.Add(new Ei.ContractPlayerInfo.Types.SeasonProgress { SeasonId = "winter_2026" });

            var mutated = AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.IsTrue(mutated);
            Assert.AreEqual(200d, account.Backup.TotalCS);
            Assert.AreEqual(75d, account.Backup.SeasonCS);
            var stored = Ei.ContractPlayerInfo.Parser.ParseFrom(account.Backup.LastContractPlayerInfoBytes);
            Assert.AreEqual(200d, stored.TotalCxp);
            Assert.AreEqual(0, stored.UnreadEvaluations.Count);
            Assert.AreEqual(0, stored.SeasonProgress.Count);
        }

        [TestMethod]
        public void Calculating_LeavesCsAndBytesAlone() {
            var (user, account) = UserWithBackup();
            var info = new Ei.ContractPlayerInfo {
                Grade = Ei.Contract.Types.PlayerGrade.GradeAaa,
                Status = Ei.ContractPlayerInfo.Types.Status.Calculating,
                TotalCxp = 0,
                SeasonCxp = 0
            };

            AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.AreEqual(100d, account.Backup.TotalCS);
            Assert.AreEqual(50d, account.Backup.SeasonCS);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, account.Backup.LastContractPlayerInfoBytes);
        }

        [TestMethod]
        public void DegenerateUnknownResponse_DoesNotZeroCs() {
            var (user, account) = UserWithBackup();
            var info = new Ei.ContractPlayerInfo();

            var mutated = AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.IsFalse(mutated);
            Assert.AreEqual(100d, account.Backup.TotalCS);
            Assert.AreEqual(50d, account.Backup.SeasonCS);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, account.Backup.LastContractPlayerInfoBytes);
        }
    }
}
