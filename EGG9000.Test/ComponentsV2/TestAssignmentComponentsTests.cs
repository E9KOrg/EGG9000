using Discord;

using EGG9000.Bot.Commands;
using EGG9000.Common.Contracts.Assignment;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test.ComponentsV2 {
    [TestClass]
    public class TestAssignmentComponentsTests {
        private static DBUser User() => new() { DiscordId = 1, EggIncAccounts = [] };

        private static EggIncAccount Account(byte group = 1) => new() {
            Id = "EI1",
            Backup = new CustomBackup { UserName = "Foo" },
            Group = group,
            Assignment = new EGG9000.Common.Contracts.Assignment.AssignmentSettings()
        };

        private static ContractFacts Contract(bool isSeasonal = false, bool isColleggtible = false) => new() {
            ContractId = "c1",
            IsLegacy = false,
            IsSeasonal = isSeasonal,
            IsUltra = false,
            IsColleggtible = isColleggtible,
            HadTwoRewards = false,
            Egg = 0,
            GradeRewards = new Dictionary<Ei.Contract.Types.PlayerGrade, GradeRewardFacts>()
        };

        private static GuildContract Gc() => new() { Created = DateTimeOffset.UtcNow.AddDays(-1), CcOnly = false, BoardingGroup = 1 };

        [TestMethod]
        public void BuildAccountBlock_Assigned_ShowsGreenBannerAndAllNamedCategories() {
            var decision = new AssignmentDecision {
                Assigned = true,
                Results = [
                    new RuleResult(AssignmentRuleId.RewardFilter, RuleTier.Include, RuleOutcome.Pass, "Reward filter"),
                    new RuleResult(AssignmentRuleId.MissingColleggtible, RuleTier.Force, RuleOutcome.NotApplicable, "Missing colleggtible"),
                    new RuleResult(AssignmentRuleId.SeasonalContracts, RuleTier.Force, RuleOutcome.NotApplicable, "Seasonal contracts"),
                    new RuleResult(AssignmentRuleId.RedoCompleted, RuleTier.Include, RuleOutcome.Pass, "Redo / previously completed"),
                    new RuleResult(AssignmentRuleId.OnBreak, RuleTier.Gate, RuleOutcome.Pass, "Break"),
                ]
            };

            var built = TestAssignmentComponents.BuildAccountBlock(User(), Account(), Contract(), decision, new Guild(), Gc(), DateTimeOffset.UtcNow).Build();
            var text = string.Join("\n", built.Components.OfType<TextDisplayComponent>().Select(t => t.Content));

            Assert.IsTrue(text.Contains("Assigned"));
            Assert.IsTrue(text.Contains("Rewards Filter"));
            Assert.IsTrue(text.Contains("Colleggtibles"));
            Assert.IsTrue(text.Contains("Seasonal"));
            Assert.IsTrue(text.Contains("Redo Leggacies"));
            Assert.IsTrue(text.Contains("Break"));
        }

        [TestMethod]
        public void BuildAccountBlock_NotAssigned_BannerShowsExclusionReason() {
            var decision = new AssignmentDecision {
                Assigned = false,
                Results = [ new RuleResult(AssignmentRuleId.OnBreak, RuleTier.Gate, RuleOutcome.Exclude, "Break") ]
            };

            var built = TestAssignmentComponents.BuildAccountBlock(User(), Account(), Contract(), decision, new Guild(), Gc(), DateTimeOffset.UtcNow).Build();
            var text = string.Join("\n", built.Components.OfType<TextDisplayComponent>().Select(t => t.Content));

            Assert.IsTrue(text.Contains("Not Assigned"));
            Assert.IsTrue(text.Contains("Break"));
        }

        [TestMethod]
        public void BuildAccountBlock_OtherCategory_OnlyShowsWhenANonSettingRuleExcludes() {
            var passing = new AssignmentDecision { Assigned = true, Results = [] };
            var builtPassing = TestAssignmentComponents.BuildAccountBlock(User(), Account(), Contract(), passing, new Guild(), Gc(), DateTimeOffset.UtcNow).Build();
            var passingText = string.Join("\n", builtPassing.Components.OfType<TextDisplayComponent>().Select(t => t.Content));
            Assert.IsFalse(passingText.Contains("**Other**"));

            var excluded = new AssignmentDecision {
                Assigned = false,
                Results = [ new RuleResult(AssignmentRuleId.AlreadyAssigned, RuleTier.Gate, RuleOutcome.Exclude, "Existing co-op") ]
            };
            var builtExcluded = TestAssignmentComponents.BuildAccountBlock(User(), Account(), Contract(), excluded, new Guild(), Gc(), DateTimeOffset.UtcNow).Build();
            var excludedText = string.Join("\n", builtExcluded.Components.OfType<TextDisplayComponent>().Select(t => t.Content));
            Assert.IsTrue(excludedText.Contains("**Other**"));
            Assert.IsTrue(excludedText.Contains("Existing co-op"));
        }

        [TestMethod]
        public void BuildAccountBlock_DisableBG_ShowsDisabledMessageNotLaunchTime() {
            var decision = new AssignmentDecision { Assigned = true, Results = [] };
            var dbguild = new Guild { DisableBG = true };

            var built = TestAssignmentComponents.BuildAccountBlock(User(), Account(), Contract(), decision, dbguild, Gc(), DateTimeOffset.UtcNow).Build();
            var text = string.Join("\n", built.Components.OfType<TextDisplayComponent>().Select(t => t.Content));

            Assert.IsTrue(text.Contains("disabled for this server"));
        }

        [TestMethod]
        public void BuildAccountBlock_NullAssignment_DoesNotThrowAndShowsSeasonalNotSet() {
            var accountWithNullAssignment = new EggIncAccount {
                Id = "EI1",
                Backup = new CustomBackup { UserName = "Foo" },
                Group = 1,
                Assignment = null
            };
            var decision = new AssignmentDecision { Assigned = true, Results = [] };

            var built = TestAssignmentComponents.BuildAccountBlock(User(), accountWithNullAssignment, Contract(), decision, new Guild(), Gc(), DateTimeOffset.UtcNow).Build();
            var text = string.Join("\n", built.Components.OfType<TextDisplayComponent>().Select(t => t.Content));

            Assert.IsTrue(text.Contains("Seasonal"));
            Assert.IsTrue(text.Contains("Not set"));
        }

        [TestMethod]
        public void BuildAccountBlock_CategoryRow_BadgeIsOnTheLeftInOneLine() {
            var decision = new AssignmentDecision {
                Assigned = true,
                Results = [ new RuleResult(AssignmentRuleId.OnBreak, RuleTier.Gate, RuleOutcome.Pass, "Break") ]
            };

            var built = TestAssignmentComponents.BuildAccountBlock(User(), Account(), Contract(), decision, new Guild(), Gc(), DateTimeOffset.UtcNow).Build();
            var lines = built.Components.OfType<TextDisplayComponent>().Select(t => t.Content).ToList();

            // Break: settings.Assignment default has no break set -> MCSBreakMessage returns some "not on break" text;
            // the important assertion is the badge/label/value are one single line, badge first.
            Assert.IsTrue(lines.Any(l => l.StartsWith("✅ **Break**:")));
            Assert.IsFalse(lines.Any(l => l.Contains('\n') && l.Contains("**Break**"))); // no longer split across two lines

            // Banner also badge-first now.
            Assert.IsTrue(lines.Any(l => l.StartsWith("✅ **Assigned**")));
        }
    }
}
