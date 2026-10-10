using EGG9000.Common.Helpers;
using System.Collections.Generic;
using static Ei.Contract.Types;

namespace EGG9000.Common.Database {
    public class CustomUniversalFarm {
        public static implicit operator CustomUniversalFarm(CustomFarm farm) {
            return new CustomUniversalFarm {
                FarmType = farm.FarmType, Artifacts = farm.Artifacts, BoostsUsed = farm.BoostsUsed, BoostTokensGiven = farm.BoostTokensGiven, BoostTokensReceived = farm.BoostTokensReceived,
                BoostTokensSpent = farm.BoostTokensSpent, Cancelled = farm.Cancelled, CashEarned = farm.CashEarned, CashSpent = farm.CashSpent, CommonResearch = farm.CommonResearch, Completed = farm.Completed, ContractId = farm.ContractId, ContributionFinalized = farm.ContributionFinalized, CoopAllowed = farm.CoopAllowed,
                CoopId = farm.CoopId, CoopSharedEndTime = farm.CoopSharedEndTime, EggsPaidFor = farm.EggsPaidFor, EggType = farm.EggType, EvaluationCxp = farm.EvaluationCxp, Grade = farm.Grade, Habs = farm.Habs, LastStepTime = farm.LastStepTime, League = farm.League, NumChickens = farm.NumChickens,
                ReportedUUIDs = farm.ReportedUUIDs, SilosOwned = farm.SilosOwned, TimeAccepted = farm.TimeAccepted, TimeCheatDebt = farm.TimeCheatDebt, TimeCheatsDetected = farm.TimeCheatsDetected, TrainLength = farm.TrainLength
            };
        }
        public static implicit operator CustomUniversalFarm(CustomArchivedFarms farm) {
            return new CustomUniversalFarm {
                CoopId = farm.CoopId, ContractId = farm.ContractId, TimeAccepted = (long)farm.TimeAccepted, Completed = farm.Completed, League = farm.League, ContributionAmount = farm.ContributionAmount,
                Grade = farm.Grade, EvaluationCxp = farm.EvaluationCxp, PEGained = farm.PEGained, PEPossible = farm.PEPossible
            };
        }

        public Ei.FarmType FarmType { get; set; }
        public string ContractId { get; set; }
        public double EggsPaidFor { get; set; }
        public uint? League { get; set; }
        public string CoopId { get; set; }
        public bool Cancelled { get; set; }
        public bool Completed { get; set; }
        public List<CustomResearch> CommonResearch { get; set; }
        public ulong NumChickens { get; set; }
        public Ei.Egg EggType { get; set; }
        public List<uint> TrainLength { get; set; }
        public List<uint> Vehicles;
        public List<EggIncArtifactInstance> Artifacts { get; set; }
        public uint SilosOwned { get; set; }
        public long TimeAccepted { get; set; }
        public bool CoopAllowed { get; set; }
        public long CoopSharedEndTime { get; set; }
        public ushort BoostTokensReceived { get; set; }
        public ushort BoostTokensGiven { get; set; }
        public ushort BoostTokensSpent { get; set; }
        public double CashEarned { get; set; }
        public double CashSpent { get; set; }
        public long TimeCheatDebt { get; set; }
        public ushort BoostsUsed { get; set; }
        public ushort TimeCheatsDetected { get; set; }
        public List<ushort> Habs { get; set; }
        public float LastStepTime { get; set; }
        public List<string> ReportedUUIDs { get; set; }
        public PlayerGrade Grade { get; set; }
        public double EvaluationCxp { get; set; }
        public bool ContributionFinalized { get; set; }
        public uint PEPossible { get; set; }
        public uint PEGained { get; set; }
        public double ContributionAmount { get; set; }
    }
}
