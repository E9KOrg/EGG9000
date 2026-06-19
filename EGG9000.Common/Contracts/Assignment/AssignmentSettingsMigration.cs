using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class AssignmentSettingsMigration {
        public static AssignmentSettings FromLegacyKeys(EggIncAccount a) {
            return new AssignmentSettings {
                ForceRules = new List<PermanentRewardRule> {
                    new() {
                        Kind = PermanentRewardKind.Colleggtible,
                        Mode = a.DoUnfinishedCollegtibles ? ForceMode.AssignIfMissing : ForceMode.NotSet
                    },
                    SeasonalPeRule(a)
                },
                NewContractRewardFilter = CleanNew(a.AutoRegisterRewards),
                LegacyRewardFilter = CleanLegacy(a.LeggacyAutoRegisterRewards),
                Redo = new RedoRule {
                    Mode = a.RedoLeggacySelection,
                    ScoreThreshold = a.RedoScoreThreshold,
                    ExcludeSeasonal = false
                },
                TwoToThree = a.DoTwoToThreeContracts
            };
        }

        private static PermanentRewardRule SeasonalPeRule(EggIncAccount a) {
            var rule = new PermanentRewardRule { Kind = PermanentRewardKind.SeasonalPe };
            switch(a.SeasonalPeOption) {
                case SeasonalPeOption.AlwaysAssignIfMissing:
                    rule.Mode = ForceMode.AssignIfMissing;
                    break;
                case SeasonalPeOption.AssignIfBelowThreshold:
                    rule.Mode = ForceMode.BelowThreshold;
                    rule.CsFloor = a.SeasonalPeThreshold;
                    break;
                // DontAssign (skip) is removed by ruling -> treat as assigned-normally.
                default:
                    rule.Mode = ForceMode.NotSet;
                    break;
            }
            return rule;
        }

        // PE dropped from the new filter; the "any reward" sentinel was never a real filter entry.
        private static List<Ei.RewardType> CleanNew(List<Ei.RewardType> source) =>
            (source ?? new List<Ei.RewardType>())
                .Where(r => r != Ei.RewardType.EggsOfProphecy && r != Ei.RewardType.UnknownReward)
                .ToList();

        // PE retained in the legacy filter; only the "any reward" sentinel is stripped.
        private static List<Ei.RewardType> CleanLegacy(List<Ei.RewardType> source) =>
            (source ?? new List<Ei.RewardType>())
                .Where(r => r != Ei.RewardType.UnknownReward)
                .ToList();
    }
}
