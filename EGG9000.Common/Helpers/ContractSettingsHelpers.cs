using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class ContractSettingsHelpers {
        public static Dictionary<Ei.RewardType, string> GetRewardDictionary() {
            return new Dictionary<Ei.RewardType, string> {
                { Ei.RewardType.EggsOfProphecy, "Eggs Of Prophecy" },
                { Ei.RewardType.Artifact, "Artifacts" },
                { Ei.RewardType.PiggyMultiplier, "Piggy Bank" },
                { Ei.RewardType.ShellScript, "Shell Tickets" },
                { Ei.RewardType.Gold, "Golden Eggs" },
                { Ei.RewardType.Boost, "Any Boost" },
                { Ei.RewardType.EpicResearchItem, "Epic Research" },
                { Ei.RewardType.UnknownReward, "** Any Reward **" },
            };
        }
    }
}
