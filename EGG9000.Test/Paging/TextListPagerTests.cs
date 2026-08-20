using EGG9000.Common.Helpers.Discord.Paging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Test.Paging {
    [TestClass]
    public class TextListPagerTests {
        private class TestPager(IReadOnlyList<string> lines, int page, int maxCharsPerPage = 1000) : TextListPager(lines, page, maxCharsPerPage) {
            protected override string Title => "Test";
            protected override string CustomIdPrefix => "Test";
            protected override string KeySuffix => "key";
        }

        [TestMethod]
        public void EmptyList_IsOnePage() {
            var pager = new TestPager([], 0);
            Assert.AreEqual(1, pager.PageCount);
        }

        [TestMethod]
        public async Task EmptyList_EmptyDescription() {
            var pager = new TestPager([], 0);
            var embed = await pager.RenderEmbedAsync();
            Assert.IsTrue(string.IsNullOrEmpty(embed.Description));
        }

        [TestMethod]
        public async Task ShortList_FitsOnOnePage_ExactJoin() {
            var lines = new List<string> { "1: a", "2: b", "3: c" };
            var pager = new TestPager(lines, 0);
            Assert.AreEqual(1, pager.PageCount);
            var embed = await pager.RenderEmbedAsync();
            Assert.AreEqual("1: a\n2: b\n3: c", embed.Description);
        }

        [TestMethod]
        public void LongList_BreaksIntoMultiplePagesAtCharBudget() {
            var lines = Enumerable.Range(0, 50).Select(_ => new string('x', 30)).ToList();
            var pager = new TestPager(lines, 0, 100);
            Assert.IsTrue(pager.PageCount > 1);
        }

        [TestMethod]
        public async Task LongList_EachPageStaysNearBudget() {
            var lines = Enumerable.Range(0, 50).Select(_ => new string('x', 30)).ToList();
            var pager = new TestPager(lines, 0, 100);
            var embed = await pager.RenderEmbedAsync();
            Assert.IsTrue(embed.Description.Length <= 130);
        }

        [TestMethod]
        public void SingleLineLongerThanBudget_GetsItsOwnPage_NoInfiniteLoop() {
            var lines = new List<string> { new string('x', 5000) };
            var pager = new TestPager(lines, 0, 100);
            Assert.AreEqual(1, pager.PageCount);
        }

        [TestMethod]
        public void RequestedPageBeyondRange_ClampsToLastPage() {
            var lines = Enumerable.Range(0, 10).Select(_ => new string('x', 30)).ToList();
            var pager = new TestPager(lines, 999, 100);
            Assert.AreEqual(pager.PageCount - 1, pager.Page);
        }

        [TestMethod]
        public void NegativeRequestedPage_ClampsToZero() {
            var lines = Enumerable.Range(0, 10).Select(_ => new string('x', 30)).ToList();
            var pager = new TestPager(lines, -5, 100);
            Assert.AreEqual(0, pager.Page);
        }

        [TestMethod]
        public async Task Footer_OmittedOnSinglePage() {
            var pager = new TestPager(new List<string> { "a" }, 0);
            var embed = await pager.RenderEmbedAsync();
            Assert.IsFalse(embed.Footer.HasValue);
        }

        [TestMethod]
        public async Task Footer_ShowsPageNumberOnMultiPage() {
            var lines = Enumerable.Range(0, 10).Select(_ => new string('x', 30)).ToList();
            var pager = new TestPager(lines, 0, 100);
            var embed = await pager.RenderEmbedAsync();
            Assert.IsTrue(embed.Footer.HasValue);
            Assert.AreEqual($"Page 1/{pager.PageCount}", embed.Footer.Value.Text);
        }

        [TestMethod]
        public async Task Preamble_PrependedWhenOverridden() {
            var pager = new PreamblePager(new List<string> { "line" }, 0);
            var embed = await pager.RenderEmbedAsync();
            Assert.AreEqual("Intro\nline", embed.Description);
        }

        private class PreamblePager(IReadOnlyList<string> lines, int page) : TextListPager(lines, page) {
            protected override string Title => "Test";
            protected override string Preamble => "Intro";
            protected override string CustomIdPrefix => "Test";
            protected override string KeySuffix => "key";
        }

        [TestMethod]
        public async Task WrapBody_WrapsRenderedBody() {
            var pager = new WrapBodyPager(new List<string> { "line" }, 0);
            var embed = await pager.RenderEmbedAsync();
            Assert.AreEqual("[[line]]", embed.Description);
        }

        private class WrapBodyPager(IReadOnlyList<string> lines, int page) : TextListPager(lines, page) {
            protected override string Title => "Test";
            protected override string WrapBody(string body) => $"[[{body}]]";
            protected override string CustomIdPrefix => "Test";
            protected override string KeySuffix => "key";
        }

        [TestMethod]
        public void ExactBudgetBoundary_FitsOnOnePageWhenExactlyAtLimit() {
            var lines = new List<string> { new string('x', 49), new string('x', 49) };
            var pager = new TestPager(lines, 0, 100);
            Assert.AreEqual(1, pager.PageCount);
        }

        [TestMethod]
        public void ExactBudgetBoundary_SplitsWhenOneCharOverLimit() {
            var lines = new List<string> { new string('x', 50), new string('x', 49) };
            var pager = new TestPager(lines, 0, 100);
            Assert.AreEqual(2, pager.PageCount);
        }
    }
}
