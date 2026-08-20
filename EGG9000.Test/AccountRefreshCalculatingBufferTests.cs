using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class AccountRefreshCalculatingBufferTests {
        private static (DBUser user, EggIncAccount account) UserWithAccount(G lastGrade) {
            var account = new EggIncAccount { Id = "EI0000000000000003", LastGrade = lastGrade };
            var user = new DBUser { DiscordUsername = "test" };
            user.EggIncAccounts = [account];
            return (user, account);
        }

        [TestMethod]
        public void Complete_AppliesGradeImmediately() {
            var (user, account) = UserWithAccount(G.GradeAaa);
            var info = new Ei.ContractPlayerInfo { Grade = G.GradeAa, Status = Ei.ContractPlayerInfo.Types.Status.Complete };

            var mutated = AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.IsTrue(mutated);
            Assert.AreEqual(G.GradeAa, account.LastGrade);
        }

        [TestMethod]
        public void Complete_ClearsAnyPendingBuffer() {
            var (user, account) = UserWithAccount(G.GradeAaa);
            account.PendingGrade = G.GradeAa;
            account.PendingGradeSince = DateTimeOffset.UtcNow;
            var info = new Ei.ContractPlayerInfo { Grade = G.GradeAaa, Status = Ei.ContractPlayerInfo.Types.Status.Complete };

            AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.IsNull(account.PendingGrade);
        }

        [TestMethod]
        public void Calculating_FirstSightingBuffersWithoutApplying() {
            var (user, account) = UserWithAccount(G.GradeAaa);
            var info = new Ei.ContractPlayerInfo { Grade = G.GradeAa, Status = Ei.ContractPlayerInfo.Types.Status.Calculating };

            AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.AreEqual(G.GradeAaa, account.LastGrade);
            Assert.AreEqual(G.GradeAa, account.PendingGrade);
        }

        [TestMethod]
        public void Calculating_SameCandidateUnderWindow_StaysBuffered() {
            var (user, account) = UserWithAccount(G.GradeAaa);
            account.PendingGrade = G.GradeAa;
            account.PendingGradeSince = DateTimeOffset.UtcNow.AddMinutes(-30);
            var info = new Ei.ContractPlayerInfo { Grade = G.GradeAa, Status = Ei.ContractPlayerInfo.Types.Status.Calculating };

            var mutated = AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.IsFalse(mutated);
            Assert.AreEqual(G.GradeAaa, account.LastGrade);
        }

        [TestMethod]
        public void Calculating_SameCandidatePastWindow_AcceptsAnyway() {
            var (user, account) = UserWithAccount(G.GradeAaa);
            account.PendingGrade = G.GradeAa;
            account.PendingGradeSince = DateTimeOffset.UtcNow.AddHours(-2);
            var info = new Ei.ContractPlayerInfo { Grade = G.GradeAa, Status = Ei.ContractPlayerInfo.Types.Status.Calculating };

            var mutated = AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.IsTrue(mutated);
            Assert.AreEqual(G.GradeAa, account.LastGrade);
            Assert.IsNull(account.PendingGrade);
        }

        [TestMethod]
        public void Calculating_CandidateChangeResetsTheClock() {
            var (user, account) = UserWithAccount(G.GradeAaa);
            account.PendingGrade = G.GradeA;
            account.PendingGradeSince = DateTimeOffset.UtcNow.AddHours(-2);
            var info = new Ei.ContractPlayerInfo { Grade = G.GradeAa, Status = Ei.ContractPlayerInfo.Types.Status.Calculating };

            AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.AreEqual(G.GradeAaa, account.LastGrade);
            Assert.AreEqual(G.GradeAa, account.PendingGrade);
            Assert.IsTrue(account.PendingGradeSince > DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        [TestMethod]
        public void Calculating_CandidateMatchingCurrentGrade_ClearsBuffer() {
            var (user, account) = UserWithAccount(G.GradeAaa);
            account.PendingGrade = G.GradeAa;
            account.PendingGradeSince = DateTimeOffset.UtcNow.AddHours(-2);
            var info = new Ei.ContractPlayerInfo { Grade = G.GradeAaa, Status = Ei.ContractPlayerInfo.Types.Status.Calculating };

            var mutated = AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.IsTrue(mutated);
            Assert.IsNull(account.PendingGrade);
        }

        [TestMethod]
        public void OutOfDate_NeverTouchesBufferState() {
            var (user, account) = UserWithAccount(G.GradeAaa);
            account.PendingGrade = G.GradeA;
            var pendingSince = DateTimeOffset.UtcNow.AddHours(-5);
            account.PendingGradeSince = pendingSince;
            var info = new Ei.ContractPlayerInfo { Grade = G.GradeAa, Status = Ei.ContractPlayerInfo.Types.Status.OutOfDate };

            var mutated = AccountRefresh.ApplyExtras(user, account, info, NullLogger.Instance);

            Assert.IsFalse(mutated);
            Assert.AreEqual(G.GradeAaa, account.LastGrade);
            Assert.AreEqual(G.GradeA, account.PendingGrade);
            Assert.AreEqual(pendingSince, account.PendingGradeSince);
        }
    }
}
