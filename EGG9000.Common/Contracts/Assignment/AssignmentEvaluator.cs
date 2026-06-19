using EGG9000.Common.Helpers;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Contracts.Assignment {
    public static class AssignmentEvaluator {
        public static AssignmentDecision Evaluate(
            AccountFacts facts,
            ContractFacts contract,
            AssignmentSettings settings,
            IReadOnlySet<AssignmentRuleId> forbidden = null,
            bool filtersDisabled = false) {

            var results = new List<RuleResult>();
            settings ??= new AssignmentSettings();

            foreach(var rule in AssignmentRuleSet.Gate) {
                if(Skip(rule, contract, forbidden)) continue;
                var outcome = rule.Evaluate(facts, contract, settings);
                Record(results, rule, outcome);
                if(outcome == RuleOutcome.Exclude) return Decision(false, results);
            }

            // DisableBG: gates still apply, but reward/redo/seasonal filters are disabled.
            if(filtersDisabled) return Decision(true, results);

            foreach(var rule in AssignmentRuleSet.Force) {
                if(Skip(rule, contract, forbidden)) continue;
                var outcome = rule.Evaluate(facts, contract, settings);
                Record(results, rule, outcome);
                if(outcome == RuleOutcome.ForceInclude) return Decision(true, results);
                if(outcome == RuleOutcome.Exclude) return Decision(false, results);
            }

            foreach(var rule in AssignmentRuleSet.Include) {
                if(Skip(rule, contract, forbidden)) continue;
                var outcome = rule.Evaluate(facts, contract, settings);
                Record(results, rule, outcome);
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

        private static bool Skip(IAssignmentRule rule, ContractFacts contract, IReadOnlySet<AssignmentRuleId> forbidden) =>
            (forbidden != null && forbidden.Contains(rule.Id)) || !rule.AppliesTo(contract);

        private static void Record(List<RuleResult> results, IAssignmentRule rule, RuleOutcome outcome) {
            if(outcome == RuleOutcome.NotApplicable) return;
            results.Add(new RuleResult(rule.Id, rule.Tier, outcome, rule.Describe(outcome)));
        }

        private static AssignmentDecision Decision(bool assigned, List<RuleResult> results) =>
            new() { Assigned = assigned, Results = results };
    }
}
