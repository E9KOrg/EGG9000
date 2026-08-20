using EGG9000.Common.Database;

using MessagePack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class CustomFarmParentDerivationTests {
        private static readonly MessagePackSerializerOptions Lz4 =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        private static (Ei.Backup backup, FrozenSet<Ei.Contract> contracts) BuildBackupWithFarm(string contractId, uint league, double timeAccepted) {
            var contract = new Ei.Contract { Identifier = contractId };
            var localContract = new Ei.LocalContract {
                Contract = contract,
                ContractIdentifier = contractId,
                League = league,
                TimeAccepted = timeAccepted,
                CoopIdentifier = "coop-xyz",
                Cancelled = true,
                CoopSharedEndTime = 1_650_500_000,
                BoostsUsed = 3,
                Grade = Ei.Contract.Types.PlayerGrade.GradeAa,
                CoopContributionFinalized = true,
                CoopSimulationEndTime = 1_650_600_000,
                NumGoalsAchieved = 2
            };
            var simulation = new Ei.Backup.Types.Simulation {
                ContractId = contractId,
                FarmType = Ei.FarmType.Contract,
                EggsPaidFor = 55.5,
                NumChickens = 12345,
                EggType = Ei.Egg.RocketFuel,
                SilosOwned = 4,
                BoostTokensReceived = 3,
                BoostTokensGiven = 2,
                BoostTokensSpent = 1,
                CashEarned = 5000.5,
                CashSpent = 1200.25,
                TimeCheatsDetected = 1,
                LastStepTime = 3.5,
                CommonResearch = { new Ei.Backup.Types.ResearchItem { Id = "hab_capacity", Level = 6 } },
                TrainLength = { 5u, 6u, 7u },
                Habs = { 10u, 20u, 30u, 40u },
                HabPopulation = { 111u, 222u, 333u, 444u }
            };

            var backup = new Ei.Backup {
                Game = new Ei.Backup.Types.Game(),
                Artifacts = new Ei.Backup.Types.Artifacts(),
                Contracts = new Ei.MyContracts { Contracts = { localContract } },
                ArtifactsDb = new Ei.ArtifactsDB()
            };
            backup.Farms.Add(simulation);

            return (backup, new List<Ei.Contract> { contract }.ToFrozenSet());
        }

        private static Ei.Contract.Types.Goal NewGoal(double targetAmount) {
            return new Ei.Contract.Types.Goal {
                Type = Ei.GoalType.EggsLaid,
                TargetAmount = targetAmount,
                RewardType = Ei.RewardType.Cash,
                RewardAmount = 1
            };
        }

        [TestMethod]
        public void CustomFarm_BackCompat_ConvertedMembers_ReturnLegacyValues_WhenBytesNull() {
            var farm = new CustomFarm {
                FarmType = Ei.FarmType.Contract,
                ContractId = "legacy-contract",
                EggsPaidFor = 12.5,
                CommonResearch = [new CustomResearch { Id = "hab_capacity", Level = 3 }],
                NumChickens = 4000,
                EggType = Ei.Egg.Tachyon,
                TrainLength = [1u, 2u, 3u],
                SilosOwned = 2,
                BoostTokensReceived = 5,
                BoostTokensGiven = 6,
                BoostTokensSpent = 1,
                CashEarned = 999.5,
                CashSpent = 250.25,
                TimeCheatDebt = 42,
                TimeCheatsDetected = 3,
                Habs = [1, 2, 3, 4],
                LastStepTime = 1.5f,
                League = 2,
                CoopId = "legacy-coop",
                Cancelled = true,
                TimeAccepted = 1_600_000_000,
                CoopSharedEndTime = 1_600_100_000,
                BoostsUsed = 7,
                Grade = Ei.Contract.Types.PlayerGrade.GradeAa,
                EvaluationCxp = 88.5,
                ContributionFinalized = true,
                CoopSimulationEndTime = 1_600_200_000,
                NumGoalsAchieved = 4
            };

            var bytes = MessagePackSerializer.Serialize(farm, Lz4);
            var back = MessagePackSerializer.Deserialize<CustomFarm>(bytes, Lz4);

            Assert.IsNull(back.SimulationBytes);
            Assert.IsNull(back.LocalContractBytes);
            Assert.AreEqual(Ei.FarmType.Contract, back.FarmType);
            Assert.AreEqual("legacy-contract", back.ContractId);
            Assert.AreEqual(12.5, back.EggsPaidFor);
            Assert.AreEqual(1, back.CommonResearch.Count);
            Assert.AreEqual("hab_capacity", back.CommonResearch[0].Id);
            Assert.AreEqual(4000UL, back.NumChickens);
            Assert.AreEqual(Ei.Egg.Tachyon, back.EggType);
            CollectionAssert.AreEqual(new List<uint> { 1u, 2u, 3u }, back.TrainLength);
            Assert.AreEqual(2u, back.SilosOwned);
            Assert.AreEqual((ushort)5, back.BoostTokensReceived);
            Assert.AreEqual((ushort)6, back.BoostTokensGiven);
            Assert.AreEqual((ushort)1, back.BoostTokensSpent);
            Assert.AreEqual(999.5, back.CashEarned);
            Assert.AreEqual(250.25, back.CashSpent);
            Assert.AreEqual(42L, back.TimeCheatDebt);
            Assert.AreEqual((ushort)3, back.TimeCheatsDetected);
            CollectionAssert.AreEqual(new List<ushort> { 1, 2, 3, 4 }, back.Habs);
            Assert.AreEqual(1.5f, back.LastStepTime);
            Assert.AreEqual((uint?)2, back.League);
            Assert.AreEqual("legacy-coop", back.CoopId);
            Assert.IsTrue(back.Cancelled);
            Assert.AreEqual(1_600_000_000L, back.TimeAccepted);
            Assert.AreEqual(1_600_100_000L, back.CoopSharedEndTime);
            Assert.AreEqual((ushort)7, back.BoostsUsed);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeAa, back.Grade);
            Assert.AreEqual(88.5, back.EvaluationCxp);
            Assert.IsTrue(back.ContributionFinalized);
            Assert.AreEqual(1_600_200_000d, back.CoopSimulationEndTime);
            Assert.AreEqual((byte)4, back.NumGoalsAchieved);
        }

        [TestMethod]
        public void CustomFarm_Derivation_PrefersDerivedOverLegacyFields() {
            var (backup, contracts) = BuildBackupWithFarm("contract-derive", 1, 1_650_000_000);

            var result = new CustomBackup(backup, contracts);
            var farm = result.Farms.Single();

            Assert.AreEqual(Ei.FarmType.Contract, farm.FarmType);
            Assert.AreEqual("contract-derive", farm.ContractId);
            Assert.AreEqual(55.5, farm.EggsPaidFor);
            Assert.AreEqual(1, farm.CommonResearch.Count);
            Assert.AreEqual("hab_capacity", farm.CommonResearch[0].Id);
            Assert.AreEqual(12345UL, farm.NumChickens);
            Assert.AreEqual(Ei.Egg.RocketFuel, farm.EggType);
            CollectionAssert.AreEqual(new List<uint> { 5u, 6u, 7u }, farm.TrainLength);
            Assert.AreEqual(4u, farm.SilosOwned);
            Assert.AreEqual((ushort)3, farm.BoostTokensReceived);
            Assert.AreEqual((ushort)2, farm.BoostTokensGiven);
            Assert.AreEqual((ushort)1, farm.BoostTokensSpent);
            Assert.AreEqual(5000.5, farm.CashEarned);
            Assert.AreEqual(1200.25, farm.CashSpent);
            Assert.AreEqual((ushort)1, farm.TimeCheatsDetected);
            CollectionAssert.AreEqual(new List<ushort> { 10, 20, 30, 40 }, farm.Habs);
            Assert.AreEqual(3.5f, farm.LastStepTime);
            Assert.AreEqual((uint?)1, farm.League);
            Assert.AreEqual("coop-xyz", farm.CoopId);
            Assert.IsTrue(farm.Cancelled);
            Assert.AreEqual(1_650_000_000L, farm.TimeAccepted);
            Assert.AreEqual(1_650_500_000L, farm.CoopSharedEndTime);
            Assert.AreEqual((ushort)3, farm.BoostsUsed);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeAa, farm.Grade);
            Assert.IsTrue(farm.ContributionFinalized);
            Assert.AreEqual(1_650_600_000d, farm.CoopSimulationEndTime);
            Assert.AreEqual((byte)2, farm.NumGoalsAchieved);

            farm.NumChickens = 999999;
            farm.League = 99;
            farm.Habs = [1, 2, 3];

            Assert.AreEqual(12345UL, farm.NumChickens);
            Assert.AreEqual((uint?)1, farm.League);
            CollectionAssert.AreEqual(new List<ushort> { 10, 20, 30, 40 }, farm.Habs);
        }

        [TestMethod]
        public void CustomFarm_SimulationBytes_ClearsHabPopulation_KeepsHabs() {
            var (backup, contracts) = BuildBackupWithFarm("contract-trim-sim", 0, 1_600_000_000);
            var result = new CustomBackup(backup, contracts);
            var farm = result.Farms.Single();

            var simulation = Ei.Backup.Types.Simulation.Parser.ParseFrom(farm.SimulationBytes);

            Assert.AreEqual(0, simulation.HabPopulation.Count);
            CollectionAssert.AreEqual(new List<uint> { 10u, 20u, 30u, 40u }, simulation.Habs);
            Assert.AreEqual("contract-trim-sim", simulation.ContractId);
            Assert.AreEqual(12345UL, simulation.NumChickens);
        }

        [TestMethod]
        public void CustomFarm_LocalContractBytes_ClearsEmbeddedContract() {
            var (backup, contracts) = BuildBackupWithFarm("contract-trim-lc", 0, 1_600_000_000);
            var result = new CustomBackup(backup, contracts);
            var farm = result.Farms.Single();

            var localContract = Ei.LocalContract.Parser.ParseFrom(farm.LocalContractBytes);

            Assert.IsNull(localContract.Contract);
            Assert.AreEqual("contract-trim-lc", localContract.ContractIdentifier);
        }

        [TestMethod]
        public void CustomArchivedFarms_BackCompat_ConvertedMembers_ReturnLegacyValues_WhenBytesNull() {
            var archived = new CustomArchivedFarms {
                CoopId = "legacy-coop",
                ContractId = "legacy-contract-id",
                TimeAccepted = 1_580_000_000f,
                League = 4,
                ContributionAmount = 321.25f,
                Grade = Ei.Contract.Types.PlayerGrade.GradeA,
                EvaluationCxp = 15.5f,
                NumGoalsAchieved = 3,
                ReportedUUIDs = ["uuid-a", "uuid-b"]
            };

            var bytes = MessagePackSerializer.Serialize(archived, Lz4);
            var back = MessagePackSerializer.Deserialize<CustomArchivedFarms>(bytes, Lz4);

            Assert.IsNull(back.LocalContractBytes);
            Assert.AreEqual("legacy-coop", back.CoopId);
            Assert.AreEqual("legacy-contract-id", back.ContractId);
            Assert.AreEqual(1_580_000_000f, back.TimeAccepted);
            Assert.AreEqual((byte?)4, back.League);
            Assert.AreEqual(321.25f, back.ContributionAmount);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeA, back.Grade);
            Assert.AreEqual(15.5f, back.EvaluationCxp);
            Assert.AreEqual((byte)3, back.NumGoalsAchieved);
            CollectionAssert.AreEqual(new List<string> { "uuid-a", "uuid-b" }, back.ReportedUUIDs);
        }

        [TestMethod]
        public void CustomArchivedFarms_Derivation_PrefersDerivedOverLegacyFields() {
            var contract = new Ei.Contract { Identifier = "contract-archived-derive" };
            var localContract = new Ei.LocalContract {
                Contract = contract,
                ContractIdentifier = "contract-archived-derive",
                CoopIdentifier = "coop-archived",
                TimeAccepted = 1_640_000_000,
                League = 3,
                CoopLastUploadedContribution = 4321.5,
                Grade = Ei.Contract.Types.PlayerGrade.GradeB,
                Evaluation = new Ei.ContractEvaluation { Cxp = 12.75 },
                NumGoalsAchieved = 2,
                ReportedUuids = { "uuid-1", "uuid-2" }
            };

            var archived = new CustomArchivedFarms(localContract);

            Assert.AreEqual("coop-archived", archived.CoopId);
            Assert.AreEqual("contract-archived-derive", archived.ContractId);
            Assert.AreEqual(1_640_000_000f, archived.TimeAccepted);
            Assert.AreEqual((byte?)3, archived.League);
            Assert.AreEqual(4321.5f, archived.ContributionAmount);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeB, archived.Grade);
            Assert.AreEqual(12.75f, archived.EvaluationCxp);
            Assert.AreEqual((byte)2, archived.NumGoalsAchieved);
            CollectionAssert.AreEqual(new List<string> { "uuid-1", "uuid-2" }, archived.ReportedUUIDs);

            archived.CoopId = "overridden";
            archived.Grade = Ei.Contract.Types.PlayerGrade.GradeAaa;

            Assert.AreEqual("coop-archived", archived.CoopId);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeB, archived.Grade);
        }

        [TestMethod]
        public void CustomArchivedFarms_LocalContractBytes_ClearsEmbeddedContract() {
            var contract = new Ei.Contract { Identifier = "contract-archived-trim" };
            var localContract = new Ei.LocalContract {
                Contract = contract,
                ContractIdentifier = "contract-archived-trim",
                League = 0
            };

            var archived = new CustomArchivedFarms(localContract);
            var parsed = Ei.LocalContract.Parser.ParseFrom(archived.LocalContractBytes);

            Assert.IsNull(parsed.Contract);
            Assert.AreEqual("contract-archived-trim", parsed.ContractIdentifier);
        }

        [TestMethod]
        public void CustomBackup_Completed_AgreesBetweenLiveAndArchivedFarms_ForGoalSetsAtLeagueIndex() {
            var contractId = "contract-completed-agree";
            var contract = new Ei.Contract { Identifier = contractId };
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(100) } });
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(100), NewGoal(200) } });
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(100), NewGoal(200), NewGoal(300) } });

            Assert.AreNotEqual(contract.GoalSets[0].Goals.Count, contract.GoalSets[2].Goals.Count);

            var localContract = new Ei.LocalContract {
                ContractIdentifier = contractId,
                League = 2,
                Grade = Ei.Contract.Types.PlayerGrade.GradeUnset,
                NumGoalsAchieved = (uint)contract.GoalSets[2].Goals.Count
            };
            var simulation = new Ei.Backup.Types.Simulation {
                ContractId = contractId,
                FarmType = Ei.FarmType.Contract
            };

            var backup = new Ei.Backup {
                Game = new Ei.Backup.Types.Game(),
                Artifacts = new Ei.Backup.Types.Artifacts(),
                Contracts = new Ei.MyContracts { Contracts = { localContract } },
                ArtifactsDb = new Ei.ArtifactsDB()
            };
            backup.Farms.Add(simulation);

            var contracts = new List<Ei.Contract> { contract }.ToFrozenSet();
            var result = new CustomBackup(backup, contracts);

            Assert.IsTrue(result.Farms.Single().Completed);
            Assert.IsTrue(result.ArchivedFarms.Single().Completed);
        }

        [TestMethod]
        public void CustomArchivedFarms_Completed_UsesGoalSetsAtLeagueIndex_NotGoalSetsZero() {
            var contract = new Ei.Contract { Identifier = "contract-league-goals" };
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(100) } });
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(100), NewGoal(200) } });
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(100), NewGoal(200), NewGoal(300) } });

            Assert.AreEqual(1, contract.GoalSets[0].Goals.Count);
            Assert.AreEqual(3, contract.GoalSets[2].Goals.Count);

            var completedContract = new Ei.LocalContract {
                Contract = contract,
                League = 2,
                NumGoalsAchieved = 3
            };
            Assert.AreEqual(3, contract.GetGoals(completedContract).Count);
            Assert.IsTrue(new CustomArchivedFarms(completedContract).Completed);

            var underAchievedContract = new Ei.LocalContract {
                Contract = contract,
                League = 2,
                NumGoalsAchieved = 1
            };
            Assert.IsFalse(new CustomArchivedFarms(underAchievedContract).Completed);
        }

        [TestMethod]
        public void CustomArchivedFarms_Completed_UsesGradeSpecs_WhenGradeSet() {
            var contract = new Ei.Contract { Identifier = "contract-grade-goals" };
            contract.GradeSpecs.Add(new Ei.Contract.Types.GradeSpec {
                Grade = Ei.Contract.Types.PlayerGrade.GradeC,
                Goals = { NewGoal(100), NewGoal(200) }
            });
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(999) } });

            var localContract = new Ei.LocalContract {
                Contract = contract,
                Grade = Ei.Contract.Types.PlayerGrade.GradeC,
                League = 0,
                NumGoalsAchieved = 2
            };

            Assert.AreEqual(2, contract.GetGoals(localContract).Count);
            Assert.IsTrue(new CustomArchivedFarms(localContract).Completed);
        }

        [TestMethod]
        public void CustomArchivedFarms_Completed_FallsBackToGoalSets_WhenGradeSetButNoGradeSpecs() {
            var contract = new Ei.Contract { Identifier = "contract-no-gradespecs" };
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(1) } });
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(1), NewGoal(2) } });

            var localContract = new Ei.LocalContract {
                Contract = contract,
                Grade = Ei.Contract.Types.PlayerGrade.GradeB,
                League = 1,
                NumGoalsAchieved = 2
            };

            var goals = contract.GetGoals(localContract);
            Assert.AreEqual(2, goals.Count);

            var archived = new CustomArchivedFarms(localContract);
            Assert.IsTrue(archived.Completed);
        }

        [TestMethod]
        public void CustomArchivedFarms_Completed_FallsBackToTopLevelGoals_WhenLeagueOutOfRange() {
            var contract = new Ei.Contract { Identifier = "contract-league-oob" };
            contract.Goals.Add(NewGoal(1));
            contract.Goals.Add(NewGoal(2));
            contract.GoalSets.Add(new Ei.Contract.Types.GoalSet { Goals = { NewGoal(1) } });

            var localContract = new Ei.LocalContract {
                Contract = contract,
                League = 5,
                NumGoalsAchieved = 2
            };

            var goals = contract.GetGoals(localContract);
            Assert.AreEqual(2, goals.Count);

            var archived = new CustomArchivedFarms(localContract);
            Assert.IsTrue(archived.Completed);
        }
    }
}
