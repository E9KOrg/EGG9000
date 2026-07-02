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

        // Legacy list wins if set, else the main list. PE is kept when migrating from the legacy filter
        // (V1 preserved it there); only the new-contract filter stripped PE (seasonal PE was handled
        // separately). Stripping PE from the legacy source was the V2 regression.
        private static List<Ei.RewardType> SingleRewardFilter(EggIncAccount a) {
            if(a.LeggacyAutoRegisterRewards is { Count: > 0 })
                return RewardMatch.Sanitize(a.LeggacyAutoRegisterRewards);
            return RewardMatch.Sanitize(a.AutoRegisterRewards, stripPe: true);
        }

        // Seasonal is mandatory in v2. Only the explicit CS-threshold option carries a mode across;
        // every other legacy state defaults to always-assign.
        private static SeasonalRule SeasonalFromOption(EggIncAccount a) {
            if(a.SeasonalPeOption == SeasonalPeOption.AssignIfBelowThreshold)
                return new SeasonalRule { Mode = SeasonalMode.UntilCsGoal, CsGoal = a.SeasonalPeThreshold, RewardFilterAfter = false };

            // NotSet, AlwaysAssignIfMissing, and DontAssign (skip removed) all become always-assign.
            return new SeasonalRule { Mode = SeasonalMode.AlwaysAssign, RewardFilterAfter = false };
        }
    }
}
