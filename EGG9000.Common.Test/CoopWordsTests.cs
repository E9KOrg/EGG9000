using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using EGG9000.Common.JsonData;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class CoopWordsTests {

        [TestMethod]
        public void Loads_a_large_word_pool() {
            var words = CoopWords.Get();
            Assert.IsNotNull(words);
            Assert.IsTrue(words.Count > 1500, $"expected an expanded pool, got {words.Count}");
        }

        [TestMethod]
        public void All_words_are_lowercase_three_to_five_chars() {
            var rx = new Regex("^[a-z]{3,5}$");
            var bad = CoopWords.Get().Where(w => !rx.IsMatch(w)).ToList();
            Assert.AreEqual(0, bad.Count, $"non-conforming entries: {string.Join(", ", bad.Take(20))}");
        }

        [TestMethod]
        public void No_duplicate_words() {
            var words = CoopWords.Get();
            var dupes = words.GroupBy(w => w).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.AreEqual(0, dupes.Count, $"duplicates: {string.Join(", ", dupes)}");
        }

        [TestMethod]
        public void Excludes_known_offender_words() {
            var offenders = new HashSet<string> {
                "bulge", "poker", "bush", "bust", "moist", "prude", "rack", "shaft",
                "slit", "wad", "drill", "nail", "mount", "ride", "stuff", "pound",
                "plow", "probe", "ooze", "slick", "jab", "crave", "flirt", "booty", "girth"
            };
            var present = CoopWords.Get().Where(offenders.Contains).ToList();
            Assert.AreEqual(0, present.Count, $"offenders still present: {string.Join(", ", present)}");
        }
    }
}
