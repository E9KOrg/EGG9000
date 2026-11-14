using EGG9000.Common.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class CoopWordsTests {
        [TestMethod]
        public void WordListHasOnlyLowercaseThreeToFiveLetterWords() {
            var bad = CoopNames.Words.Where(w => w.Length is < 3 or > 5 || !w.All(c => c is >= 'a' and <= 'z')).ToList();
            Assert.IsEmpty(bad, string.Join(", ", bad));
        }

        [TestMethod]
        public void WordListHasNoDuplicates() {
            var dupes = CoopNames.Words.GroupBy(w => w).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dupes, string.Join(", ", dupes));
        }

        [TestMethod]
        public void SecondWordDoesNotRepeatBoundaryLetter() {
            for(var i = 0; i < 500; i++) {
                var first = CoopNames.RandomWord();
                var second = CoopNames.RandomSecondWord(first);
                Assert.AreNotEqual(char.ToLowerInvariant(first[^1]), char.ToLowerInvariant(second[0]), first + second);
            }
        }

        [TestMethod]
        public void SecondWordDoesNotStartWithLOrIAfterLOrI() {
            foreach(var first in new[] { "Coral", "Kiwi" }) {
                for(var i = 0; i < 500; i++) {
                    var second = CoopNames.RandomSecondWord(first);
                    Assert.IsFalse(second[0] is 'L' or 'I', first + second);
                }
            }
        }

        [TestMethod]
        public void NumberNeverContains69() {
            for(var i = 0; i < 5000; i++) {
                Assert.DoesNotContain("69", CoopNames.RandomNumber());
                Assert.DoesNotContain("69", CoopNames.RandomNumber(wide: true));
            }
        }
    }
}
