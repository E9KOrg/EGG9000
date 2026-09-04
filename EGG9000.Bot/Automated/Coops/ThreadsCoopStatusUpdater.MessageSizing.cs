using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using static EGG9000.Common.Helpers.FixedWidthTable;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        private const int RosterRowCharsWorstCase = 150;
        private const int UnjoinedMentionCharsWorstCase = 62;
        private const int UnjoinedListOverheadCharsWorstCase = 70;
        private const int GiftTableCharsWorstCase = 550;
        private const int CommandLinksCharsWorstCase = 350;
        private const int GradeWarningCharsWorstCase = 150;
        private const int EmbedHeaderCharsWorstCase = 500;
        public const int V1MessageContentCharBudget = 2000;

        private static int EstimateWorstCaseTextChars(int maxUsers) {
            var rosterChars = (maxUsers + 1) * RosterRowCharsWorstCase;
            var unjoinedChars = UnjoinedListOverheadCharsWorstCase + maxUsers * UnjoinedMentionCharsWorstCase;
            return rosterChars + unjoinedChars + GiftTableCharsWorstCase + CommandLinksCharsWorstCase + GradeWarningCharsWorstCase;
        }

        public static int EstimateWorstCaseMessageSlots(int maxUsers) {
            var textSlots = (int)Math.Ceiling(EstimateWorstCaseTextChars(maxUsers) / (double)V1MessageContentCharBudget);
            return 1 + textSlots;
        }

        public static int EstimateWorstCaseMessageSlotsV2(int maxUsers) {
            var totalChars = EstimateWorstCaseTextChars(maxUsers) + EmbedHeaderCharsWorstCase;
            return (int)Math.Ceiling(totalChars / (double)ComponentsV2Safe.TextDisplayMax);
        }

        internal static string BuildWorstCaseFillerText(int maxUsers) =>
            BuildWorstCaseUnjoinedList(maxUsers) + BuildWorstCaseGiftTable() + BuildWorstCaseCommandLinks() + BuildWorstCaseGradeWarning();

        internal static string BuildWorstCaseHeaderText() =>
            "# TESTSEED-WorstCaseCoopName99\nGrade AAA - simulated worst-case coop status embed\n-# Updated just now - simulated worst-case content, not a real coop";

        private static string BuildWorstCaseUnjoinedList(int maxUsers) {
            var mentions = Enumerable.Range(0, maxUsers).Select(i =>
                Truncate($"<@{100000000000000000L + i}> (WorstCaseFarmerName{i}) (Wrong GradeAAA) (Missing from server)", UnjoinedMentionCharsWorstCase));
            return $"Coop **TESTSEED-WorstCaseCoopName99** is ready for the following to join: {string.Join(", ", mentions)}\n";
        }

        private static string BuildWorstCaseGiftTable() {
            List<List<FixedWidthCell>> table = [
                [new(""), new("🐔", CellAlignment.Center), new("🏠", CellAlignment.Center), new("🚚", CellAlignment.Center)],
                .. Enumerable.Range(0, 10).Select(i => new List<FixedWidthCell> {
                    new(Truncate($"WorstCasePlyr{i}", 11)),
                    new("999.99q", CellAlignment.Right),
                    new("99%", CellAlignment.Right),
                    new("99%", CellAlignment.Right),
                }),
            ];
            return $"\nFarms that would benefit from gifting chickens: \n```{GetTable(table)}```\n\n";
        }

        private static string BuildWorstCaseCommandLinks() =>
            "__Co-op Commands (click to use):__\n" +
            "\n</callstaff:999999999999999999> Use this command if you joined a co-op for the wrong contract, or have other questions or concerns" +
            "\n</coopsettings:999999999999999999> Receive DM pings for various events in the co-op" +
            "\n</fixfullcooperror:999999999999999999> If you get the error co-op is full, try running this command to free up the space.";

        private static string BuildWorstCaseGradeWarning() =>
            " Warning! Looks like this co-op is the wrong grade and is actually GradeAAA" +
            "\n\nWaiting on the following users to check-in: <@100000000000000000>, <@100000000000000001>, <@100000000000000002>";
    }
}
