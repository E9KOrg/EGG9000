using Discord;

using EGG9000.Common.Helpers.Discord.Paging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Test.Paging {
    [TestClass]
    public class PagerTests {
        private class TestPager(int page, int pageCount) : Pager(page) {
            public override int PageCount { get; } = pageCount;
            protected override string CustomIdPrefix => "Prefix";
            protected override string KeySuffix => "key";
            public override Task<Embed> RenderEmbedAsync() => Task.FromResult(new EmbedBuilder().Build());

            public ButtonBuilder ExposedPrev() => PrevButton();
            public ButtonBuilder ExposedNext() => NextButton();
        }

        [TestMethod]
        public void PrevButton_DisabledOnFirstPage() {
            var pager = new TestPager(0, 3);
            Assert.IsTrue(pager.ExposedPrev().IsDisabled);
        }

        [TestMethod]
        public void PrevButton_EnabledAfterFirstPage() {
            var pager = new TestPager(1, 3);
            Assert.IsFalse(pager.ExposedPrev().IsDisabled);
        }

        [TestMethod]
        public void NextButton_DisabledOnLastPage() {
            var pager = new TestPager(2, 3);
            Assert.IsTrue(pager.ExposedNext().IsDisabled);
        }

        [TestMethod]
        public void NextButton_EnabledBeforeLastPage() {
            var pager = new TestPager(1, 3);
            Assert.IsFalse(pager.ExposedNext().IsDisabled);
        }

        [TestMethod]
        public void Buttons_EncodePrefixKeySuffixAndAdjacentPage() {
            var pager = new TestPager(1, 3);
            Assert.AreEqual("Prefix:key,0", pager.ExposedPrev().CustomId);
            Assert.AreEqual("Prefix:key,2", pager.ExposedNext().CustomId);
        }

        [TestMethod]
        public void BuildComponents_SinglePage_ReturnsNull() {
            var pager = new TestPager(0, 1);
            Assert.IsNull(pager.BuildComponents());
        }

        [TestMethod]
        public void BuildComponents_MultiPage_ReturnsOneRowWithBothButtons() {
            var pager = new TestPager(1, 3);
            var built = pager.BuildComponents();
            Assert.IsNotNull(built);
            var row = (ActionRowComponent)built.Components.Single();
            Assert.AreEqual(2, row.Components.Count);
        }

        [TestMethod]
        public async Task RenderAsync_ReturnsEmbedAndComponents() {
            var pager = new TestPager(0, 2);
            var (embed, components) = await pager.RenderAsync();
            Assert.IsNotNull(embed);
            Assert.IsNotNull(components);
        }
    }
}
