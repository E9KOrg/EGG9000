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
            s.Seasonal ??= new SeasonalRule();
            s.Redo ??= new RedoRule();
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
                case "seasonalMode":
                    if(!int.TryParse(value, out var sm) || !Enum.IsDefined(typeof(SeasonalMode), sm)) return Bad();
                    return Set(() => s.Seasonal.Mode = (SeasonalMode)sm);
                case "seasonalCsGoal":
                    if(!double.TryParse(value, out var goal) || goal < 0) return Bad();
                    return Set(() => s.Seasonal.CsGoal = goal);
                case "seasonalRewardFilterAfter":
                    return ParseBool(value, out var after) ? Set(() => s.Seasonal.RewardFilterAfter = after) : Bad();
                case "rewardFilter":
                    return ParseRewards(value, out var rf)
                        ? Set(() => s.RewardFilter = rf.Where(r => r != Ei.RewardType.EggsOfProphecy && r != Ei.RewardType.UnknownReward).ToList())
                        : Bad();
                default:
                    return new ContractSettingResult(ContractSettingApplyStatus.UnknownField);
            }
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
