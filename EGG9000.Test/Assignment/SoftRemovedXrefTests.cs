using EGG9000.Common.Contracts.Assignment.Facts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

namespace EGG9000.Test.Assignment {
    [TestClass]
    [TestCategory("Unit")]
    public class SoftRemovedXrefTests {

        private static DBContract DbContract() {
            var contract = new Ei.Contract { Identifier = "test-contract", Name = "Test Contract" };
            for(var g = 1; g <= 5; g++) {
                var spec = new Ei.Contract.Types.GradeSpec {
                    Grade = (Ei.Contract.Types.PlayerGrade)g,
                    LengthSeconds = (long)TimeSpan.FromDays(2).TotalSeconds
                };
                spec.Goals.Add(new Ei.Contract.Types.Goal { TargetAmount = 1_000_000_000 });
                contract.GradeSpecs.Add(spec);
            }
            var c = new DBContract();
            c.OverwriteDetails(contract);
            return c;
        }

        // Soft-removed xrefs must keep counting as assigned, otherwise a kicked user
        // would be re-placed by a later boarding group.
        [TestMethod]
        public void SoftRemovedXref_StillCountsAsAlreadyAssigned() {
            var contract = DbContract();
            var user = new DBUser {
                Id = Guid.NewGuid(),
                DiscordUsername = "tester",
                Registered = DateTimeOffset.UtcNow.AddYears(-1)
            };
            var account = new EggIncAccount {
                Id = "EI0001",
                Name = "EI0001",
                Backup = new CustomBackup { EggIncId = "EI0001" }
            };
            var coop = new Coop {
                Name = "test-coop",
                ContractID = contract.ID,
                UserCoopsXrefs = [
                    new UserCoopXref {
                        UserId = user.Id,
                        EggIncId = "EI0001",
                        JoinedCoop = false,
                        Removed = true,
                        RemovedOn = DateTimeOffset.UtcNow,
                        CreatedOn = DateTimeOffset.UtcNow.AddHours(-20)
                    }
                ]
            };

            var facts = AccountFactsBuilder.Build(user, account, contract, [coop], null, null, null);

            Assert.IsTrue(facts.AlreadyAssigned, "Soft-removed xref must still mark the account as already assigned.");
        }
    }
}
