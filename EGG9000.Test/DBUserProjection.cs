using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test {
    // Guards the InactivePlayers / register projection change. Both call sites stopped loading the
    // full Users row and now project only the account-id columns, rebuilding a DBUser via
    // DBUser.FromAccountColumns. These tests pin that the account-id set is identical to a full
    // entity, i.e. dropping _CustomBackups / _shipDMsByte / scalar columns changes nothing the
    // callers read.
    [TestClass]
    public class DBUserProjectionTests {
        private static List<string> AccountIds(DBUser u) => u.EggIncAccounts.Select(a => a.Id).ToList();

        [TestMethod]
        public void MessagePackPathYieldsSameAccountIds() {
            var source = new DBUser {
                EggIncAccounts = new List<EggIncAccount> {
                    new() { Id = "EI111", Name = "A" },
                    new() { Id = "EI222", Name = "B" }
                }
            };
            // Setter stored accounts into _contractRegistrationByte; this is what the DB persists.
            var projected = DBUser.FromAccountColumns(source._eggIncIds, source._contractRegistrationByte);

            CollectionAssert.AreEqual(AccountIds(source), AccountIds(projected));
            CollectionAssert.AreEqual(new[] { "EI111", "EI222" }, AccountIds(projected));
        }

        [TestMethod]
        public void LegacyJsonPathYieldsSameAccountIds() {
            // Users migrated before the MessagePack column have accounts only in _eggIncIds.
            var source = new DBUser {
                _eggIncIds = "[{\"Id\":\"EI333\"},{\"Id\":\"EI444\"}]",
                _contractRegistrationByte = null
            };
            var projected = DBUser.FromAccountColumns(source._eggIncIds, source._contractRegistrationByte);

            CollectionAssert.AreEqual(AccountIds(source), AccountIds(projected));
            CollectionAssert.AreEqual(new[] { "EI333", "EI444" }, AccountIds(projected));
        }

        [TestMethod]
        public void CustomBackupsBlobDoesNotChangeAccountIds() {
            // The full entity may carry a _CustomBackups blob the projection drops. It only hydrates
            // account.Backup, never account.Id, so the id set must be unchanged.
            var full = new DBUser {
                EggIncAccounts = new List<EggIncAccount> {
                    new() { Id = "EI555", Name = "C" }
                },
                _CustomBackups = MessagePackSerializer.Serialize(new List<CustomBackup>(), DBUser.lz4Options)
            };
            var fullIds = AccountIds(full); // exercises the _CustomBackups branch in the getter

            var projected = DBUser.FromAccountColumns(full._eggIncIds, full._contractRegistrationByte);

            CollectionAssert.AreEqual(fullIds, AccountIds(projected));
            CollectionAssert.AreEqual(new[] { "EI555" }, AccountIds(projected));
        }

        [TestMethod]
        public void NullColumnsYieldNoAccounts() {
            var projected = DBUser.FromAccountColumns(null, null);
            Assert.AreEqual(0, projected.EggIncAccounts.Count);
        }

        // The EggIncAccounts getter syncs LastGrade from the most-recent backup contract grade. That
        // sync may only catch the grade up, never push it down. A lower-grade backup contract accepted
        // after PromotionTime is a grade pull (ULTRA all-grade coop join), not a demotion - it must not
        // overwrite a higher LastGrade. These build the user via the persisted columns so the getter's
        // backup-hydration + grade-sync branch runs.
        private static DBUser UserWithBackupGrade(G lastGrade, long promotionTimeUnix, G backupGrade, long backupAccepted) {
            var source = new DBUser {
                EggIncAccounts = new List<EggIncAccount> {
                    new() {
                        Id = "EI777",
                        Name = "Z",
                        LastGrade = lastGrade,
                        PromotionTime = DateTimeOffset.FromUnixTimeSeconds(promotionTimeUnix)
                    }
                }
            };
            source._CustomBackups = MessagePackSerializer.Serialize(new List<CustomBackup> {
                new() {
                    EggIncId = "EI777",
                    Farms = [new CustomFarm { Grade = backupGrade, TimeAccepted = backupAccepted }]
                }
            }, DBUser.lz4Options);
            // Rehydrate from the persisted columns so the getter runs the backup-hydration + grade-sync
            // branch (the in-memory _accounts cache on `source` would otherwise short-circuit it).
            return new DBUser {
                _eggIncIds = source._eggIncIds,
                _contractRegistrationByte = source._contractRegistrationByte,
                _CustomBackups = source._CustomBackups
            };
        }

        [TestMethod]
        public void LowerGradeBackupContractDoesNotDemoteLastGrade() {
            var user = UserWithBackupGrade(G.GradeAaa, 1_000_000_000, G.GradeAa, 2_000_000_000);
            Assert.AreEqual(G.GradeAaa, user.EggIncAccounts.Single().LastGrade);
        }

        [TestMethod]
        public void HigherGradeBackupContractCatchesLastGradeUp() {
            var user = UserWithBackupGrade(G.GradeAa, 1_000_000_000, G.GradeAaa, 2_000_000_000);
            Assert.AreEqual(G.GradeAaa, user.EggIncAccounts.Single().LastGrade);
        }
    }
}
