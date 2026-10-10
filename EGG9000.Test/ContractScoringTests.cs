using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ContractScoringTests {

        private const uint LeagueGradeAaa = 5;

        private static Ei.Contract ContractWithLongGrade() {
            var contract = new Ei.Contract { Identifier = "test-contract", Name = "Test Contract" };
            for(var g = 1; g <= 5; g++) {
                var spec = new Ei.Contract.Types.GradeSpec {
                    Grade = (Ei.Contract.Types.PlayerGrade)g,
                    LengthSeconds = (long)TimeSpan.FromDays(2).TotalSeconds
                };
                spec.Goals.Add(new Ei.Contract.Types.Goal { TargetAmount = 1_000_000_000 });
                contract.GradeSpecs.Add(spec);
            }
            return contract;
        }

        private static DBContract DbContract() {
            var c = new DBContract();
            c.OverwriteDetails(ContractWithLongGrade());
            return c;
        }

        private static DBUser UserWithAccounts(params string[] eggIncIds) {
            var accounts = eggIncIds.Select(id => new EggIncAccount { Id = id, Name = id }).ToList();
            return new DBUser {
                Id = Guid.NewGuid(),
                DiscordUsername = "tester",
                Registered = DateTimeOffset.UtcNow.AddYears(-1),
                _eggIncIds = JsonConvert.SerializeObject(accounts)
            };
        }

        private static Coop CoopFor(DBContract contract, DBUser user, string eggIncId, double eggsShipped, double soulPower) {
            var coop = new Coop {
                Name = $"coop-{eggIncId}",
                ContractID = contract.ID,
                League = LeagueGradeAaa,
                LastStatusUpdate = new Ei.ContractCoopStatusResponse()
            };
            var xref = new UserCoopXref {
                UserId = user.Id,
                EggIncId = eggIncId,
                JoinedCoop = true,
                User = user,
                Coop = coop,
                CreatedOn = DateTimeOffset.UtcNow,
                LastStatus = new ContributionInfoCompact {
                    ContributionAmount = eggsShipped,
                    SoulPower = soulPower
                }
            };
            coop.UserCoopsXrefs = [xref];
            return coop;
        }

        [TestMethod]
        public void MultiAccountUser_GetsOneScorePerAccount() {
            var contract = DbContract();
            var user = UserWithAccounts("EI0001", "EI0002");

            var coops = new List<Coop> {
                CoopFor(contract, user, "EI0001", eggsShipped: 500_000_000, soulPower: 10),
                CoopFor(contract, user, "EI0002", eggsShipped: 200_000_000, soulPower: 12),
            };

            var scores = ContractScoring.GetContractScores(coops, contract, NullLogger.Instance);

            var scoredAccounts = scores.Select(s => s.xref.EggIncId).OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(new[] { "EI0001", "EI0002" }, scoredAccounts,
                $"Each account should be scored once; got [{string.Join(", ", scoredAccounts)}].");
        }
    }
}
