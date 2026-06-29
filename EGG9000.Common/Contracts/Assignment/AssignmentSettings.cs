using EGG9000.Common.Helpers;

using MessagePack;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    [MessagePackObject]
    public class AssignmentSettings {
        [Key(0)] public List<PermanentRewardRule> ForceRules { get; set; } = new();
        [Key(1)] public List<Ei.RewardType> RewardFilter { get; set; } = new();
        // Retained (unused) from the v1 model so old blobs still deserialize; superseded by RewardFilter.
        [Key(2)] public List<Ei.RewardType> LegacyRewardFilter { get; set; } = new();
        [Key(3)] public RedoRule Redo { get; set; } = new();
        [Key(4)] public bool TwoToThree { get; set; }
        [Key(5)] public SeasonalRule Seasonal { get; set; } = new();

        public PermanentRewardRule Get(PermanentRewardKind kind) =>
            ForceRules?.FirstOrDefault(r => r.Kind == kind) ?? new PermanentRewardRule { Kind = kind, Mode = ForceMode.NotSet };

        // Upserts a force rule. Get returns a detached default for reads; writes must land in the list.
        public void SetForce(PermanentRewardKind kind, ForceMode mode, double? csFloor = null) {
            ForceRules ??= new();
            var rule = ForceRules.FirstOrDefault(r => r.Kind == kind);
            if(rule is null) {
                rule = new PermanentRewardRule { Kind = kind };
                ForceRules.Add(rule);
            }
            rule.Mode = mode;
            rule.CsFloor = csFloor;
        }
    }

    [MessagePackObject]
    public class PermanentRewardRule {
        [Key(0)] public PermanentRewardKind Kind { get; set; }
        [Key(1)] public ForceMode Mode { get; set; } = ForceMode.NotSet;
        [Key(2)] public double? CsFloor { get; set; }
    }

    [MessagePackObject]
    public class RedoRule {
        [Key(0)] public RedoLeggacyOption Mode { get; set; } = RedoLeggacyOption.NotSet;
        [Key(1)] public int ScoreThreshold { get; set; } = 20000;
        [Key(2)] public bool ExcludeSeasonal { get; set; }
    }

    // Seasonal contracts are mandatory (no off). Until* modes force-assign until the condition is met;
    // RewardFilterAfter then decides whether the normal reward filter governs (true) or assignment
    // stops for the rest of the season (false).
    [MessagePackObject]
    public class SeasonalRule {
        [Key(0)] public SeasonalMode Mode { get; set; } = SeasonalMode.AlwaysAssign;
        [Key(1)] public double CsGoal { get; set; }
        [Key(2)] public bool RewardFilterAfter { get; set; }

        // Grade-based minimum CS goal for the UntilCsGoal seasonal mode. Anti-dodge: a too-low (or 0)
        // goal would clear the seasonal force on the first run, letting players skip the Monday
        // seasonals. The floor is enforced both at input (modal) and at evaluation (EffectiveCsGoal),
        // so blobs stored before this floor existed cannot dodge either. Applies ONLY to this seasonal
        // CS input, not to any other CS threshold in the system.
        public static double CsGoalFloor(Ei.Contract.Types.PlayerGrade grade) => grade switch {
            Ei.Contract.Types.PlayerGrade.GradeAaa => 200_000,
            Ei.Contract.Types.PlayerGrade.GradeAa => 100_000,
            Ei.Contract.Types.PlayerGrade.GradeA => 50_000,
            Ei.Contract.Types.PlayerGrade.GradeB => 10_000,
            Ei.Contract.Types.PlayerGrade.GradeC => 5_000,
            _ => 5_000
        };

        // The CS goal actually applied for this grade: never below the grade floor.
        public double EffectiveCsGoal(Ei.Contract.Types.PlayerGrade grade) =>
            System.Math.Max(CsGoal, CsGoalFloor(grade));
    }
}
