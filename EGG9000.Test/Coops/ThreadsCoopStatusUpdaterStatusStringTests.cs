using EGG9000.Bot.Automated.Coops;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;

using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Test.Coops {
    // GetStatusStringAsync builds the coop status table. It is static and pure (no Discord/EF), but
    // walks the full UserFarmDetails projection and the FixedWidthTable formatter. Participants here
    // carry a backup but no matching farm (contract IDs differ) so the heavy WithStats path is
    // skipped and the derived props fall back to their no-farm/no-status defaults.
    [TestClass]
    public class ThreadsCoopStatusUpdaterStatusStringTests {
        private const string ContractId = "egg-day-x";

        private static DBContract ContractWithGoal(int maxUsers = 10) {
            var contract = new DBContract { ID = ContractId, MaxUsers = maxUsers };
            var details = new Ei.Contract();
            var goalSet = new Ei.Contract.Types.GoalSet();
            goalSet.Goals.Add(new Ei.Contract.Types.Goal { TargetAmount = 1_000_000_000 });
            details.GoalSets.Add(goalSet);
            contract.OverwriteDetails(details);
            return contract;
        }

        private static UserFarmDetails Participant(string userName, double soulEggs) {
            var backup = new CustomBackup {
                EggIncId = "EI-" + userName,
                UserName = userName,
                SoulEggs = soulEggs,
                Farms = [new CustomFarm { ContractId = "other-contract" }],
                ArchivedFarms = []
            };
            var user = new DBUser { GuildId = 0, EggIncAccounts = [], DiscordUsername = userName };
            var uwb = new UserWithBackup { User = user, Backup = backup };
            // ID differs from the farm ContractId so Farm resolves null and WithStats is skipped.
            return new UserFarmDetails(new DBContract { ID = "unmatched" }, uwb, [], null, 0);
        }

        private static CoopDetails DetailsWith(List<UserFarmDetails> participants, DBContract contract) {
            var guildContract = new GuildContract { ContractID = ContractId, Contract = contract };
            return new CoopDetails(participants, guildContract, 0) {
                Coop = new Coop { Name = "satpot60", ContractID = ContractId }
            };
        }

        [TestMethod]
        public void Returns_at_least_one_message_block() {
            var contract = ContractWithGoal();
            var details = DetailsWith([Participant("satpot", 1e12)], contract);

            var msgs = ThreadsCoopStatusUpdater.GetStatusStringAsync(details, contract);

            Assert.IsNotNull(msgs);
            Assert.IsTrue(msgs.Count >= 1);
        }

        [TestMethod]
        public void Header_shows_joined_over_max_users() {
            var contract = ContractWithGoal(maxUsers: 15);
            var details = DetailsWith([Participant("a", 1e12), Participant("b", 2e12)], contract);

            var msgs = ThreadsCoopStatusUpdater.GetStatusStringAsync(details, contract);

            // Two participants against a 15-user cap.
            Assert.IsTrue(string.Concat(msgs).Contains("2/15"));
        }

        [TestMethod]
        public void Table_includes_each_participant_name() {
            var contract = ContractWithGoal();
            var details = DetailsWith([Participant("kendrome", 5e12), Participant("azural", 3e12)], contract);

            var joined = string.Concat(ThreadsCoopStatusUpdater.GetStatusStringAsync(details, contract));

            Assert.IsTrue(joined.Contains("kendrome"));
            Assert.IsTrue(joined.Contains("azural"));
        }

        [TestMethod]
        public void Every_block_is_within_the_discord_2000_char_limit() {
            // Many participants force the >2000-char pagination loop to split the table.
            var contract = ContractWithGoal(maxUsers: 50);
            var participants = Enumerable.Range(0, 40)
                .Select(i => Participant($"user{i:D2}", 1e12 + i))
                .ToList();
            var details = DetailsWith(participants, contract);

            var msgs = ThreadsCoopStatusUpdater.GetStatusStringAsync(details, contract);

            Assert.IsTrue(msgs.Count >= 1);
            foreach(var block in msgs) {
                Assert.IsTrue(block.Length <= 2000, $"block length {block.Length} exceeds 2000");
            }
        }
    }
}
