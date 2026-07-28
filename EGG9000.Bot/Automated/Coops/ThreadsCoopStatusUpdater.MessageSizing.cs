using System;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        private const int RosterRowCharsWorstCase = 150;
        private const int UnjoinedMentionCharsWorstCase = 62;
        private const int UnjoinedListOverheadCharsWorstCase = 70;
        private const int GiftTableCharsWorstCase = 550;
        private const int CommandLinksCharsWorstCase = 350;
        private const int GradeWarningCharsWorstCase = 150;
        private const int V1MessageContentCharBudget = 2000;

        public static int EstimateWorstCaseMessageSlots(int maxUsers) {
            var rosterChars = (maxUsers + 1) * RosterRowCharsWorstCase;
            var unjoinedChars = UnjoinedListOverheadCharsWorstCase + maxUsers * UnjoinedMentionCharsWorstCase;
            var totalTextChars = rosterChars + unjoinedChars + GiftTableCharsWorstCase + CommandLinksCharsWorstCase + GradeWarningCharsWorstCase;

            var textSlots = (int)Math.Ceiling(totalTextChars / (double)V1MessageContentCharBudget);
            return 1 + textSlots;
        }
    }
}
