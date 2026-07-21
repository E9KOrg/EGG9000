using EGG9000.Bot.Automated.Coops;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Ei;

using static EGG9000.Test.Coops.CoopUpdaterTestHelpers;

namespace EGG9000.Test.Coops {
    // Pure/static helpers on ThreadsCoopStatusUpdater with no Discord/EF/network dependency.
    // These are the fully unit-testable surface of the file in its current form; the rest of the
    // class reaches Discord, EF, and the send queue through instance fields with no injection seam.
    [TestClass]
    public class ThreadsCoopStatusUpdaterPureTests {
        [TestMethod]
        public void Truncate_null_or_empty_passes_through() {
            Assert.IsNull(ThreadsCoopStatusUpdater.Truncate(null, 5));
            Assert.AreEqual("", ThreadsCoopStatusUpdater.Truncate("", 5));
        }

        [TestMethod]
        public void Truncate_shorter_than_max_is_unchanged() {
            Assert.AreEqual("abc", ThreadsCoopStatusUpdater.Truncate("abc", 5));
        }

        [TestMethod]
        public void Truncate_equal_to_max_is_unchanged() {
            Assert.AreEqual("abcde", ThreadsCoopStatusUpdater.Truncate("abcde", 5));
        }

        [TestMethod]
        public void Truncate_longer_than_max_is_cut() {
            Assert.AreEqual("abcde", ThreadsCoopStatusUpdater.Truncate("abcdefgh", 5));
        }

        [TestMethod]
        public void Truncate_zero_max_yields_empty() {
            Assert.AreEqual("", ThreadsCoopStatusUpdater.Truncate("abc", 0));
        }

        [TestMethod]
        [DataRow(12345, 1, 5)]
        [DataRow(12345, 2, 4)]
        [DataRow(12345, 3, 3)]
        [DataRow(12345, 5, 1)]
        public void GetDigit_returns_nth_digit_from_the_right(int number, int digit, int expected) {
            Assert.AreEqual(expected, ThreadsCoopStatusUpdater.GetDigit(number, digit));
        }

        [TestMethod]
        public void GetDigit_past_most_significant_digit_is_zero() {
            Assert.AreEqual(0, ThreadsCoopStatusUpdater.GetDigit(42, 4));
        }

        [TestMethod]
        public void GetDigit_first_digit_of_single_digit_number() {
            Assert.AreEqual(7, ThreadsCoopStatusUpdater.GetDigit(7, 1));
        }

        [TestMethod]
        public void LevenshteinDistance_identical_strings_is_zero() {
            Assert.AreEqual(0, InvokePrivateStatic<int>("LevenshteinDistance", "kendrome", "kendrome"));
        }

        [TestMethod]
        public void LevenshteinDistance_one_substitution() {
            Assert.AreEqual(1, InvokePrivateStatic<int>("LevenshteinDistance", "cat", "bat"));
        }

        [TestMethod]
        public void LevenshteinDistance_insertions_and_deletions() {
            Assert.AreEqual(3, InvokePrivateStatic<int>("LevenshteinDistance", "kitten", "sitting"));
        }

        [TestMethod]
        public void LevenshteinDistance_empty_to_nonempty_is_length() {
            Assert.AreEqual(4, InvokePrivateStatic<int>("LevenshteinDistance", "", "abcd"));
        }

        [TestMethod]
        public void LevenshteinRatio_identical_is_100() {
            Assert.AreEqual(100, InvokePrivateStatic<int>("LevenshteinRatio", "satpot", "satpot"));
        }

        [TestMethod]
        public void LevenshteinRatio_both_empty_is_100() {
            Assert.AreEqual(100, InvokePrivateStatic<int>("LevenshteinRatio", "", ""));
        }

        [TestMethod]
        public void LevenshteinRatio_completely_different_is_zero() {
            // No shared characters, equal length -> distance == length -> ratio 0.
            Assert.AreEqual(0, InvokePrivateStatic<int>("LevenshteinRatio", "abc", "xyz"));
        }

        [TestMethod]
        public void LevenshteinRatio_partial_match_scales() {
            // "brother" vs "brxther": 1 edit over 7 chars -> ~86%.
            Assert.AreEqual(86, InvokePrivateStatic<int>("LevenshteinRatio", "brother", "brxther"));
        }

        [TestMethod]
        public void GetTachyonAmount_no_contributors_is_zero() {
            var result = InvokePrivateStatic<decimal>(
                "GetTachyonAmount",
                Contributors(),
                "self");
            Assert.AreEqual(0m, result);
        }

        [TestMethod]
        public void GetTachyonAmount_excludes_the_current_user() {
            // Only the self row has a buff; it must be skipped, leaving zero.
            var contributors = Contributors(Contributor("self", 2.5));
            var result = InvokePrivateStatic<decimal>("GetTachyonAmount", contributors, "self");
            Assert.AreEqual(0m, result);
        }

        [TestMethod]
        public void GetTachyonAmount_ignores_contributors_with_no_buff_history() {
            var contributors = Contributors(Contributor("other"));
            var result = InvokePrivateStatic<decimal>("GetTachyonAmount", contributors, "self");
            Assert.AreEqual(0m, result);
        }

        [TestMethod]
        public void GetTachyonAmount_sums_last_buff_minus_one_per_other() {
            // rate 1.20 and 1.30 -> (0.20 + 0.30) = 0.50 total.
            var contributors = Contributors(
                Contributor("a", 1.20),
                Contributor("b", 1.30),
                Contributor("self", 5.0));
            var result = InvokePrivateStatic<decimal>("GetTachyonAmount", contributors, "self");
            Assert.AreEqual(0.50m, result);
        }

        [TestMethod]
        public void GetTachyonAmount_uses_only_the_latest_buff_entry() {
            // History 1.10 then 1.40 -> only the last (1.40) counts -> 0.40.
            var contributors = Contributors(Contributor("a", 1.10, 1.40));
            var result = InvokePrivateStatic<decimal>("GetTachyonAmount", contributors, "self");
            Assert.AreEqual(0.40m, result);
        }
    }
}
