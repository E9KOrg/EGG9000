using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test {
    [TestClass]
    public class GetGradeTests {
        private const long OldAccept = 1_000_000_000; // ~2001
        private const long NewAccept = 2_000_000_000; // ~2033

        private static EggIncAccount AccountWith(G lastGrade, DateTimeOffset promotionTime, G backupGrade, long backupAccepted) {
            return new EggIncAccount {
                LastGrade = lastGrade,
                PromotionTime = promotionTime,
                Backup = new CustomBackup {
                    Farms = [new CustomFarm { Grade = backupGrade, TimeAccepted = backupAccepted }]
                }
            };
        }

        [TestMethod]
        public void Backup_null_returns_last_grade() {
            var account = new EggIncAccount { LastGrade = G.GradeAaa, Backup = null };
            Assert.AreEqual(G.GradeAaa, account.GetGrade());
        }

        [TestMethod]
        public void Promotion_newer_than_contract_wins() {
            // Player promoted AA -> AAA via the API after their most recent contract was accepted at AA.
            // This is the Mysqk case: without a stamped PromotionTime the stale AA contract would win.
            var account = AccountWith(G.GradeAaa, DateTimeOffset.FromUnixTimeSeconds(NewAccept), G.GradeAa, OldAccept);
            Assert.AreEqual(G.GradeAaa, account.GetGrade());
        }

        [TestMethod]
        public void Contract_newer_than_promotion_wins() {
            // A contract accepted after the last known promotion is the fresher signal.
            var account = AccountWith(G.GradeAa, DateTimeOffset.FromUnixTimeSeconds(OldAccept), G.GradeAaa, NewAccept);
            Assert.AreEqual(G.GradeAaa, account.GetGrade());
        }
    }
}
