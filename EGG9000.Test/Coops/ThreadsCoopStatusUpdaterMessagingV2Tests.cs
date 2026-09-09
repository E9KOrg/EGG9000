using EGG9000.Bot.Automated.Coops;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

namespace EGG9000.Test.Coops {
    [TestClass]
    public class ThreadsCoopStatusUpdaterMessagingV2Tests {
        [TestMethod]
        public void PackTextChunksForComponentsV2_SmallChunksCombineIntoOneMessage() {
            var chunks = new[] { "a", "b", "c" };

            var packed = ThreadsCoopStatusUpdater.PackTextChunksForComponentsV2(chunks, budget: 100);

            Assert.AreEqual(1, packed.Count);
            Assert.AreEqual("a\nb\nc", packed[0]);
        }

        [TestMethod]
        public void PackTextChunksForComponentsV2_SplitsWhenBudgetExceeded() {
            var chunkA = new string('a', 60);
            var chunkB = new string('b', 60);

            var packed = ThreadsCoopStatusUpdater.PackTextChunksForComponentsV2([chunkA, chunkB], budget: 100);

            Assert.AreEqual(2, packed.Count);
            Assert.AreEqual(chunkA, packed[0]);
            Assert.AreEqual(chunkB, packed[1]);
        }

        [TestMethod]
        public void PackTextChunksForComponentsV2_NeverExceedsBudgetPerMessage() {
            var chunks = Enumerable.Range(0, 20).Select(i => new string((char)('a' + i % 26), 137)).ToList();

            var packed = ThreadsCoopStatusUpdater.PackTextChunksForComponentsV2(chunks, budget: 4000);

            Assert.IsTrue(packed.All(p => p.Length <= 4000));
        }

        [TestMethod]
        public void PackTextChunksForComponentsV2_SingleChunkOverBudgetIsTruncated() {
            var tooLong = new string('a', 150);

            var packed = ThreadsCoopStatusUpdater.PackTextChunksForComponentsV2([tooLong], budget: 100);

            Assert.AreEqual(1, packed.Count);
            Assert.AreEqual(100, packed[0].Length);
        }

        [TestMethod]
        public void PackTextChunksForComponentsV2_EmptyInputYieldsNoMessages() {
            var packed = ThreadsCoopStatusUpdater.PackTextChunksForComponentsV2([], budget: 100);

            Assert.AreEqual(0, packed.Count);
        }
    }
}
