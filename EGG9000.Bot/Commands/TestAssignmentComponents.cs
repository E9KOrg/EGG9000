using Discord;

using EGG9000.Common.Contracts;
using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using System;
using System.Linq;

namespace EGG9000.Bot.Commands {
    public static class TestAssignmentComponents {
        private static readonly AssignmentRuleId[] OtherCategoryIds = [
            AssignmentRuleId.GradeUnset, AssignmentRuleId.BackupMissing, AssignmentRuleId.UserDisabled,
            AssignmentRuleId.NoSubscription, AssignmentRuleId.InsufficientSoulEggs, AssignmentRuleId.EggLocked,
            AssignmentRuleId.AlreadyFarming, AssignmentRuleId.AlreadyAssigned
        ];

        public static ContainerBuilder BuildAccountBlock(DBUser dbuser, EggIncAccount account, ContractFacts contractFacts, AssignmentDecision decision, Guild dbguild, GuildContract guildContract, DateTimeOffset nowUtc) {
            var settings = account.Assignment ?? new AssignmentSettings();
            var accountLabel = $"{account.Backup?.UserName ?? "[unnamed]"} {account.Backup?.EarningsBonus.ToEggString()}";

            var container = new ContainerBuilder()
                .WithAccentColor(decision.Assigned ? Color.Green : Color.Red)
                .WithHeader($"Test Assignment: {accountLabel}");

            container.WithTextDisplay(decision.Assigned ? "✅ **Assigned**" : $"❌ **Not Assigned** — {decision.ExclusionReason}");

            var group = account.GetGroup(contractFacts.IsUltra);
            string bgLine;
            if(dbguild.DisableBG) {
                bgLine = "**BG:** Boarding groups disabled for this server";
            } else if(group == 0) {
                bgLine = "**BG:** Not set";
            } else {
                var pickupBg = decision.Assigned
                    ? BoardingGroupLaunch.NextPickupBoardingGroup(guildContract.Created, guildContract.CcOnly, group, nowUtc)
                    : null;
                if(pickupBg is int bg) {
                    var (_, launchTime) = BoardingGroupLaunch.GetLaunchInfo(guildContract.Created, guildContract.CcOnly, bg, nowUtc);
                    bgLine = $"**BG:** BG{bg} — launches <t:{launchTime.ToUnixTimeSeconds()}:t>";
                } else {
                    var (launched, launchTime) = BoardingGroupLaunch.GetLaunchInfo(guildContract.Created, guildContract.CcOnly, group, nowUtc);
                    bgLine = $"**BG:** BG{group} — {(launched ? "already launched" : $"launches <t:{launchTime.ToUnixTimeSeconds()}:t>")}";
                }
            }
            container.WithTextDisplay(bgLine);
            container.WithSeparator();

            RuleResult Find(AssignmentRuleId id) => decision.Results.FirstOrDefault(r => r.Rule == id);

            void CategoryRow(string label, RuleResult result, string value) {
                var badge = result == null ? "N/A" : result.Outcome switch {
                    RuleOutcome.Pass => "✅",
                    RuleOutcome.ForceInclude => "✅",
                    RuleOutcome.Exclude => "❌",
                    _ => "N/A"
                };
                container.WithTextDisplay($"{badge} **{label}**: {value}");
            }

            var rewardDict = ContractSettingsCommands.GetRewardDictionary();
            var rewards = settings.RewardFilter.Any() ? string.Join(", ", settings.RewardFilter.Select(x => rewardDict[x])) : "All";
            CategoryRow("Rewards Filter", Find(AssignmentRuleId.RewardFilter), rewards);

            var colleggtibleOn = settings.Get(PermanentRewardKind.Colleggtible).Mode == ForceMode.AssignIfMissing;
            CategoryRow("Colleggtibles", Find(AssignmentRuleId.MissingColleggtible), colleggtibleOn ? "Yes" : "No");

            CategoryRow("Seasonal", Find(AssignmentRuleId.SeasonalContracts), account.Assignment is null ? "Not set" : ContractSettingsCommands.SeasonalSummary(account));

            var redoSummary = settings.Redo.Mode switch {
                RedoLeggacyOption.YesAll => "Yes (all)",
                RedoLeggacyOption.YesNoUltra => "Yes (no ultra)",
                RedoLeggacyOption.YesThreshold => $"Yes (<{settings.Redo.ScoreThreshold:N0})",
                RedoLeggacyOption.YesOtherAccountMatch => "Yes (alt match)",
                _ => "No"
            };
            CategoryRow("Redo Leggacies / 2→3", Find(AssignmentRuleId.RedoCompleted), $"{redoSummary} (2→3: {(settings.TwoToThree ? "Yes" : "No")})");

            CategoryRow("Break", Find(AssignmentRuleId.OnBreak), ContractSettingsCommands.MCSBreakMessage(account));

            var otherFailures = decision.Results.Where(r => OtherCategoryIds.Contains(r.Rule) && r.Outcome == RuleOutcome.Exclude).ToList();
            if(otherFailures.Count > 0) {
                container.WithSeparator();
                container.WithTextDisplay("**Other**\n" + string.Join("\n", otherFailures.Select(r => $"❌ {AssignmentEvaluator.RuleLabels[r.Rule]}")));
            }

            return container;
        }
    }
}
