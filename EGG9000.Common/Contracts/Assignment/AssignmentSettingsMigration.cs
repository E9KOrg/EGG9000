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
                    }
                },
                RewardFilter = SingleRewardFilter(a),
                LegacyRewardFilter = new List<Ei.RewardType>(),
                Seasonal = SeasonalFromOption(a),
                Redo = new RedoRule {
                    Mode = a.RedoLeggacySelection,
                    ScoreThreshold = a.RedoScoreThreshold,
                    ExcludeSeasonal = false
                },
                TwoToThree = a.DoTwoToThreeContracts
            };
        }

        // Legacy list wins if set, else the main list; PE and the "any reward" sentinel are stripped.
        private static List<Ei.RewardType> SingleRewardFilter(EggIncAccount a) {
            var source = a.LeggacyAutoRegisterRewards is { Count: > 0 } ? a.LeggacyAutoRegisterRewards : a.AutoRegisterRewards;
            return (source ?? new List<Ei.RewardType>())
                .Where(r => r != Ei.RewardType.EggsOfProphecy && r != Ei.RewardType.UnknownReward)
                .ToList();
        }

        // Seasonal is mandatory in v2. The old "skip"/not-set states all migrate to assign-until-PE.
        private static SeasonalRule SeasonalFromOption(EggIncAccount a) {
            if(a.SeasonalPeOption == SeasonalPeOption.AssignIfBelowThreshold)
                return new SeasonalRule { Mode = SeasonalMode.UntilCsGoal, CsGoal = a.SeasonalPeThreshold, RewardFilterAfter = false };

            // NotSet, AlwaysAssignIfMissing, and DontAssign (skip removed) all become assign-until-PE.
            return new SeasonalRule { Mode = SeasonalMode.UntilPeEarned, RewardFilterAfter = false };
        }
    }
}
