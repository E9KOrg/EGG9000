using Ei;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace EGG9000.Common.Database.Entities {
    public class SeasonInfo {
        [Key]
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset StartTime { get; set; }
        /// <summary>
        /// JSON-serialized Dictionary&lt;int, List&lt;SeasonPeGoal&gt;&gt; keyed by (int)Contract.Types.PlayerGrade.
        /// Only stores goals where RewardType == EggsOfProphecy.
        /// </summary>
        public string GoalsJson { get; set; } = string.Empty;

        public static SeasonInfo FromProto(ContractSeasonInfo proto) {
            var goals = new Dictionary<int, List<SeasonPeGoal>>();
            foreach (var gs in proto.GradeGoals) {
                var peGoals = gs.Goals
                    .Where(g => g.RewardType == RewardType.EggsOfProphecy)
                    .Select(g => new SeasonPeGoal { Cxp = g.Cxp, PeAmount = (int)Math.Round(g.RewardAmount) })
                    .ToList();
                if (peGoals.Count > 0)
                    goals[(int)gs.Grade] = peGoals;
            }
            return new SeasonInfo {
                Id = proto.Id,
                Name = proto.Name,
                StartTime = DateTimeOffset.UnixEpoch.AddSeconds(proto.StartTime),
                GoalsJson = JsonConvert.SerializeObject(goals)
            };
        }

        public static bool HasPeRewards(ContractSeasonInfo proto) =>
            proto.GradeGoals.Any(gs => gs.Goals.Any(g => g.RewardType == RewardType.EggsOfProphecy && g.Cxp > 0));

        private Dictionary<int, List<SeasonPeGoal>> _goals;
        private Dictionary<int, List<SeasonPeGoal>> Goals =>
            _goals ??= JsonConvert.DeserializeObject<Dictionary<int, List<SeasonPeGoal>>>(GoalsJson ?? "{}") ?? [];

        // Season goals are set based on the starting grade, so even if player is demoted or promoted during the season, the goals will stay at what starting grade was.
        public int GetPeEarned(Ei.Contract.Types.PlayerGrade grade, double totalCxp) {
            if (!Goals.TryGetValue((int)grade, out var gradeGoals))
                return 0;
            return gradeGoals.Where(g => totalCxp >= g.Cxp).Sum(g => g.PeAmount);
        }

        public int GetMaxPe(Ei.Contract.Types.PlayerGrade grade) {
            if (!Goals.TryGetValue((int)grade, out var gradeGoals))
                return 0;
            return gradeGoals.Sum(g => g.PeAmount);
        }
    }

    public class SeasonPeGoal {
        public double Cxp { get; set; }
        public int PeAmount { get; set; }
    }
}
