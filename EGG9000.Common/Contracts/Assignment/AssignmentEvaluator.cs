using EGG9000.Common.Helpers;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class AssignmentEvaluator {
        // verbose = full diagnostic trace: records every rule including not-applicable and
        // server-disabled ones (used by the site "test my settings" tool). Non-verbose is the live
        // path and records only decisive/applied outcomes, byte-identical to before.
        public static AssignmentDecision Evaluate(
            AccountFacts facts,
            ContractFacts contract,
            AssignmentSettings settings,
            IReadOnlySet<AssignmentRuleId> forbidden = null,
            bool filtersDisabled = false,
            bool verbose = false) {

            var results = new List<RuleResult>();
            settings ??= new AssignmentSettings();

            foreach(var rule in AssignmentRuleSet.Gate) {
                if(SkipRecorded(results, rule, contract, forbidden, verbose)) continue;
                var outcome = rule.Evaluate(facts, contract, settings);
                Record(results, rule, outcome, verbose);
                if(outcome == RuleOutcome.Exclude) return Decision(false, results);
            }

            // DisableBG: gates still apply, but reward/redo/seasonal filters are disabled.
            if(filtersDisabled) return Decision(true, results);

            foreach(var rule in AssignmentRuleSet.Force) {
                if(SkipRecorded(results, rule, contract, forbidden, verbose)) continue;
                var outcome = rule.Evaluate(facts, contract, settings);
                Record(results, rule, outcome, verbose);
                if(outcome == RuleOutcome.ForceInclude) return Decision(true, results);
                if(outcome == RuleOutcome.Exclude) return Decision(false, results);
            }

            foreach(var rule in AssignmentRuleSet.Include) {
                if(SkipRecorded(results, rule, contract, forbidden, verbose)) continue;
                var outcome = rule.Evaluate(facts, contract, settings);
                Record(results, rule, outcome, verbose);
                if(outcome == RuleOutcome.Exclude) return Decision(false, results);
            }

            return Decision(true, results);
        }

        public static IReadOnlyList<(AccountFacts facts, AssignmentDecision decision)> EvaluateUser(
            IReadOnlyList<(AccountFacts facts, AssignmentSettings settings)> accounts,
            ContractFacts contract,
            IReadOnlySet<AssignmentRuleId> forbidden = null,
            bool filtersDisabled = false) {

            // Pass 1: provisional decisions with YesOtherAccountMatch unresolved (sibling flag false).
            var provisional = accounts
                .Select(a => (a.facts, a.settings, decision: Evaluate(a.facts, contract, a.settings, forbidden, filtersDisabled)))
                .ToList();

            // Resolve sibling matches for YesOtherAccountMatch accounts, then re-evaluate them.
            var final = new List<(AccountFacts, AssignmentDecision)>();
            foreach(var entry in provisional) {
                var mode = (entry.settings?.Redo ?? new RedoRule()).Mode;
                if(mode != RedoLeggacyOption.YesOtherAccountMatch) {
                    final.Add((entry.facts, entry.decision));
                    continue;
                }

                var siblingMatch = provisional.Any(other =>
                    !ReferenceEquals(other.facts, entry.facts) &&
                    other.decision.Assigned &&
                    GradeMatch(entry.facts, other.facts, contract) &&
                    other.facts.BoardingGroup == entry.facts.BoardingGroup);

                if(!siblingMatch) {
                    final.Add((entry.facts, entry.decision));
                    continue;
                }

                entry.facts.SiblingMatchProvisionalInclude = true;
                final.Add((entry.facts, Evaluate(entry.facts, contract, entry.settings, forbidden, filtersDisabled)));
            }

            return final;
        }

        private static bool GradeMatch(AccountFacts self, AccountFacts other, ContractFacts contract) =>
            self.Grade == other.Grade || (contract.IsUltra && self.HasActiveSubscription && other.HasActiveSubscription);

        public static readonly IReadOnlyDictionary<AssignmentRuleId, string> RuleLabels = new Dictionary<AssignmentRuleId, string> {
            [AssignmentRuleId.GradeUnset] = "Grade",
            [AssignmentRuleId.BackupMissing] = "Backup",
            [AssignmentRuleId.UserDisabled] = "Account enabled",
            [AssignmentRuleId.OnBreak] = "Break",
            [AssignmentRuleId.NoSubscription] = "Subscription",
            [AssignmentRuleId.InsufficientSoulEggs] = "Soul eggs",
            [AssignmentRuleId.EggLocked] = "Egg unlocked",
            [AssignmentRuleId.AlreadyFarming] = "Active farm",
            [AssignmentRuleId.AlreadyAssigned] = "Existing co-op",
            [AssignmentRuleId.MissingColleggtible] = "Missing colleggtible",
            [AssignmentRuleId.MissingSeasonalPe] = "Seasonal PE",
            [AssignmentRuleId.NewRewardFilter] = "New reward filter",
            [AssignmentRuleId.LegacyRewardFilter] = "Leggacy reward filter",
            [AssignmentRuleId.RedoCompleted] = "Redo / previously completed",
            [AssignmentRuleId.TwoToThree] = "Bump 2 to 3"
        };

        private static string Label(AssignmentRuleId id) => RuleLabels.TryGetValue(id, out var l) ? l : id.ToString();

        // Returns true if the rule is skipped. In verbose mode the skip is recorded with a reason.
        private static bool SkipRecorded(List<RuleResult> results, IAssignmentRule rule, ContractFacts contract, IReadOnlySet<AssignmentRuleId> forbidden, bool verbose) {
            if(forbidden != null && forbidden.Contains(rule.Id)) {
                if(verbose) results.Add(new RuleResult(rule.Id, rule.Tier, RuleOutcome.NotApplicable, $"{Label(rule.Id)}: disabled by server"));
                return true;
            }
            if(!rule.AppliesTo(contract)) {
                if(verbose) results.Add(new RuleResult(rule.Id, rule.Tier, RuleOutcome.NotApplicable, $"{Label(rule.Id)}: does not apply to this contract"));
                return true;
            }
            return false;
        }

        private static void Record(List<RuleResult> results, IAssignmentRule rule, RuleOutcome outcome, bool verbose) {
            if(outcome == RuleOutcome.NotApplicable && !verbose) return;
            var reason = verbose ? Label(rule.Id) : rule.Describe(outcome);
            results.Add(new RuleResult(rule.Id, rule.Tier, outcome, reason));
        }

        private static AssignmentDecision Decision(bool assigned, List<RuleResult> results) =>
            new() { Assigned = assigned, Results = results };
    }
}
