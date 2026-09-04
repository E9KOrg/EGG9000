using MessagePack;
using System.Collections.Generic;
using System.Linq;
using static Ei.Contract.Types;

namespace EGG9000.Common.Database {
    [MessagePackObject]
    public class CustomArchivedFarms : CustomFarmBase {
        [Key(0)]
        public string CoopId { get; set; }
        [Key(1)]
        public string ContractId { get; set; }
        [Key(2)]
        public float TimeAccepted { get; set; }
        [Key(3)]
        public bool Completed { get; set; }
        [Key(4)]
        public byte? League { get; set; }
        [Key(5)]
        public byte PEPossible { get; set; }
        [Key(6)]
        public byte PEGained { get; set; }
        [Key(7)]
        public float ContributionAmount { get; set; }
        [Key(8)]
        public PlayerGrade Grade { get; set; }
        [Key(9)]
        public float EvaluationCxp { get; set; }
        [Key(10)]
        public byte NumGoalsAchieved { get; set; }
        [Key(11)]
        public List<string> ReportedUUIDs { get; set; }

        protected override byte[] LocalContractBytesStorage => null;

        protected override long TimeAcceptedUnix => (long)TimeAccepted;

        public CustomArchivedFarms() { }
        public CustomArchivedFarms(Ei.LocalContract localContract) {
            CoopId = localContract.CoopIdentifier;
            ContractId = localContract.Contract?.Identifier;
            TimeAccepted = (float)localContract.TimeAccepted;
            League = (byte)localContract.League;
            ContributionAmount = (float)localContract.CoopLastUploadedContribution;
            Grade = localContract.Grade;
            EvaluationCxp = localContract.Evaluation is { } e ? (float)e.Cxp : 0f;
            NumGoalsAchieved = (byte)localContract.NumGoalsAchieved;
            ReportedUUIDs = [.. localContract.ReportedUuids];
            var goals = localContract.Contract is not null ? localContract.Contract.GetGoals(localContract) : null;
            Completed = localContract.Contract is not null && localContract.NumGoalsAchieved == goals.Count;
            if(goals is not null) {
                PEPossible = (byte)goals.Where(x => x.RewardType == Ei.RewardType.EggsOfProphecy).Sum(x => x.RewardAmount);
                PEGained = (byte)goals.Where(x => x.RewardType == Ei.RewardType.EggsOfProphecy && goals.IndexOf(x) < localContract.NumGoalsAchieved).Sum(x => x.RewardAmount);
            }
        }
    }
}
