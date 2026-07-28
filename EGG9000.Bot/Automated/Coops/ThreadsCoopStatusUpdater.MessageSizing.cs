using Discord;
using System;

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

        internal static string BuildWorstCaseFillerText(int maxUsers) {
            var unjoinedChars = UnjoinedListOverheadCharsWorstCase + maxUsers * UnjoinedMentionCharsWorstCase;
            var extrasChars = unjoinedChars + GiftTableCharsWorstCase + CommandLinksCharsWorstCase + GradeWarningCharsWorstCase;
            return new string('X', extrasChars);
        }

        internal static string BuildWorstCaseHeaderText() => new string('X', EmbedHeaderCharsWorstCase);
    }
}
