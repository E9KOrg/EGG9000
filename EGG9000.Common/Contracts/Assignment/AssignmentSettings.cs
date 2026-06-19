using EGG9000.Common.Helpers;

using MessagePack;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    [MessagePackObject]
    public class AssignmentSettings {
        [Key(0)] public List<PermanentRewardRule> ForceRules { get; set; } = new();
        [Key(1)] public List<Ei.RewardType> NewContractRewardFilter { get; set; } = new();
        [Key(2)] public List<Ei.RewardType> LegacyRewardFilter { get; set; } = new();
        [Key(3)] public RedoRule Redo { get; set; } = new();
        [Key(4)] public bool TwoToThree { get; set; }

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
}
