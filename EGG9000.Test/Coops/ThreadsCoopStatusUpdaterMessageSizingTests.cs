using EGG9000.Bot.Automated.Coops;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test.Coops {
    [TestClass]
    public class ThreadsCoopStatusUpdaterMessageSizingTests {
        [TestMethod]
        public void EstimateWorstCaseMessageSlots_AlwaysReservesEmbedSlot() {
            Assert.IsTrue(ThreadsCoopStatusUpdater.EstimateWorstCaseMessageSlots(0) >= 1);
        }

        [TestMethod]
        public void EstimateWorstCaseMessageSlots_GrowsWithMaxUsers() {
            var small = ThreadsCoopStatusUpdater.EstimateWorstCaseMessageSlots(4);
            var large = ThreadsCoopStatusUpdater.EstimateWorstCaseMessageSlots(70);

            Assert.IsTrue(large > small);
        }

        [TestMethod]
        public void EstimateWorstCaseMessageSlots_IsDeterministic() {
            var first = ThreadsCoopStatusUpdater.EstimateWorstCaseMessageSlots(40);
            var second = ThreadsCoopStatusUpdater.EstimateWorstCaseMessageSlots(40);

            Assert.AreEqual(first, second);
        }

        [TestMethod]
        public void EstimateWorstCaseMessageSlots_TypicalCoopFitsInFewerSlotsThanOldStaticFour() {
            var typicalCoopMaxUsers = 20;

            Assert.IsTrue(ThreadsCoopStatusUpdater.EstimateWorstCaseMessageSlots(typicalCoopMaxUsers) <= 4);
        }
    }
}
