using EGG9000.Common.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public enum ContractSettingApplyStatus { Ok, UnknownField, BadValue }

    public sealed record ContractSettingResult(ContractSettingApplyStatus Status);

    // Pure mapping from a (field, value) pair to an AssignmentSettings mutation. Shared by the site
    // autosave endpoint (and its tests). String value is parsed per field; invalid input is reported,
    // not thrown.
    public static class ContractSettingField {
        private const int MaxRedoThreshold = 90000;

        public static ContractSettingResult Apply(AssignmentSettings s, string field, string value) {
            switch(field) {
                case "colleggtible":
                    return ParseBool(value, out var coll)
                        ? Set(() => s.SetForce(PermanentRewardKind.Colleggtible, coll ? ForceMode.AssignIfMissing : ForceMode.NotSet))
                        : Bad();
                case "twoToThree":
                    return ParseBool(value, out var t23) ? Set(() => s.TwoToThree = t23) : Bad();
                case "excludeSeasonal":
                    return ParseBool(value, out var es) ? Set(() => s.Redo.ExcludeSeasonal = es) : Bad();
                case "redoMode":
                    if(!int.TryParse(value, out var rm) || !Enum.IsDefined(typeof(RedoLeggacyOption), rm)) return Bad();
                    return Set(() => s.Redo.Mode = (RedoLeggacyOption)rm);
                case "redoThreshold":
                    if(!int.TryParse(value, out var rt) || rt < 0 || rt > MaxRedoThreshold) return Bad();
                    return Set(() => s.Redo.ScoreThreshold = rt);
                case "seasonalPeMode":
                    return ApplySeasonalPeMode(s, value);
                case "seasonalPeFloor":
                    if(!double.TryParse(value, out var floor) || floor < 0) return Bad();
                    return Set(() => s.SetForce(PermanentRewardKind.SeasonalPe, ForceMode.BelowThreshold, floor));
                case "newRewardFilter":
                    return ParseRewards(value, out var nf)
                        ? Set(() => s.NewContractRewardFilter = nf.Where(r => r != Ei.RewardType.EggsOfProphecy && r != Ei.RewardType.UnknownReward).ToList())
                        : Bad();
                case "legacyRewardFilter":
                    return ParseRewards(value, out var lf)
                        ? Set(() => s.LegacyRewardFilter = lf.Where(r => r != Ei.RewardType.UnknownReward).ToList())
                        : Bad();
                default:
                    return new ContractSettingResult(ContractSettingApplyStatus.UnknownField);
            }
        }

        private static ContractSettingResult ApplySeasonalPeMode(AssignmentSettings s, string value) {
            if(!int.TryParse(value, out var v)) return Bad();
            // 0/1/2 only; 3 (skip) is removed and rejected.
            var mode = v switch {
                0 => ForceMode.NotSet,
                1 => ForceMode.AssignIfMissing,
                2 => ForceMode.BelowThreshold,
                _ => (ForceMode?)null
            };
            if(mode is null) return Bad();
            var existingFloor = s.Get(PermanentRewardKind.SeasonalPe).CsFloor;
            return Set(() => s.SetForce(PermanentRewardKind.SeasonalPe, mode.Value, mode == ForceMode.BelowThreshold ? existingFloor : null));
        }

        private static bool ParseBool(string value, out bool result) => bool.TryParse(value, out result);

        private static bool ParseRewards(string value, out List<Ei.RewardType> rewards) {
            rewards = new List<Ei.RewardType>();
            if(string.IsNullOrWhiteSpace(value)) return true;
            foreach(var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                if(!int.TryParse(token, out var n) || !Enum.IsDefined(typeof(Ei.RewardType), n)) return false;
                rewards.Add((Ei.RewardType)n);
            }
            return true;
        }

        private static ContractSettingResult Set(Action mutate) {
            mutate();
            return new ContractSettingResult(ContractSettingApplyStatus.Ok);
        }

        private static ContractSettingResult Bad() => new(ContractSettingApplyStatus.BadValue);
    }
}
