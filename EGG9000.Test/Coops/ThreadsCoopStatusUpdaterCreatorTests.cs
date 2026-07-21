using EGG9000.Bot.Automated.Coops;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Runtime.CompilerServices;

using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Test.Coops {
    // CheckForCreator is an instance method but reads no instance fields, only its two params. We
    // build the updater without its DI constructor (GetUninitializedObject) so no Discord/EF wiring
    // is needed, and drive it purely through Coop + CoopDetails.
    [TestClass]
    public class ThreadsCoopStatusUpdaterCreatorTests {
        private const string CoopName = "satpot60";
        private const string ContractId = "egg-day-x";

        private static ThreadsCoopStatusUpdater Updater() =>
            (ThreadsCoopStatusUpdater)RuntimeHelpers.GetUninitializedObject(typeof(ThreadsCoopStatusUpdater));

        // A participant whose backup holds one farm. contractId here differs from the CoopDetails
        // contract ID so UserFarmDetails skips WithStats (Farm resolves to null), keeping the build
        // free of game-statics; CheckForCreator still reads Backup.Farms directly.
        private static UserFarmDetails Participant(bool creator, string coopId = CoopName, string farmContractId = ContractId) {
            var backup = new CustomBackup {
                EggIncId = "EI0001",
                Farms = [new CustomFarm { Creator = creator, CoopId = coopId, ContractId = farmContractId }],
                ArchivedFarms = []
            };
            var user = new DBUser { GuildId = 0, EggIncAccounts = [] };
            var uwb = new UserWithBackup { User = user, Backup = backup };
            var contract = new DBContract { ID = "unmatched-contract" };
            return new UserFarmDetails(contract, uwb, [], null, 0);
        }

        private static DBContract ContractWithGoal() {
            var contract = new DBContract { ID = ContractId, MaxUsers = 10 };
            var details = new Ei.Contract();
            var goalSet = new Ei.Contract.Types.GoalSet();
            goalSet.Goals.Add(new Ei.Contract.Types.Goal { TargetAmount = 1_000_000 });
            details.GoalSets.Add(goalSet);
            contract.OverwriteDetails(details);
            return contract;
        }

        private static CoopDetails DetailsWith(List<UserFarmDetails> participants) {
            var guildContract = new GuildContract {
                ContractID = ContractId,
                Contract = ContractWithGoal()
            };
            return new CoopDetails(participants, guildContract, 0) {
                Coop = new Coop { Name = CoopName, ContractID = ContractId, CreatorID = null }
            };
        }

        [TestMethod]
        public void Sets_creator_id_when_a_creator_farm_matches() {
            var details = DetailsWith([Participant(creator: true)]);
            var updater = Updater();

            var changed = updater.CheckForCreator(details.Coop, details);

            Assert.IsTrue(changed);
            Assert.AreEqual("EI0001", details.Coop.CreatorID);
        }

        [TestMethod]
        public void Does_nothing_when_creator_id_already_set() {
            var details = DetailsWith([Participant(creator: true)]);
            details.Coop.CreatorID = "already-there";
            var updater = Updater();

            var changed = updater.CheckForCreator(details.Coop, details);

            Assert.IsFalse(changed);
            Assert.AreEqual("already-there", details.Coop.CreatorID);
        }

        [TestMethod]
        public void Does_nothing_when_no_participant_is_creator() {
            var details = DetailsWith([Participant(creator: false)]);
            var updater = Updater();

            var changed = updater.CheckForCreator(details.Coop, details);

            Assert.IsFalse(changed);
            Assert.IsNull(details.Coop.CreatorID);
        }

        [TestMethod]
        public void Does_not_match_a_creator_farm_from_a_different_coop() {
            var details = DetailsWith([Participant(creator: true, coopId: "some-other-coop")]);
            var updater = Updater();

            var changed = updater.CheckForCreator(details.Coop, details);

            Assert.IsFalse(changed);
            Assert.IsNull(details.Coop.CreatorID);
        }
    }
}
