using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    public class CoopMessageSenderLaneTests {
        [TestMethod]
        public void Lane_is_stable_for_the_same_thread_across_calls() {
            Assert.AreEqual(CoopMessageSender.IsWebhookLane(1001ul), CoopMessageSender.IsWebhookLane(1001ul));
            Assert.AreEqual(CoopMessageSender.IsWebhookLane(1000ul), CoopMessageSender.IsWebhookLane(1000ul));
        }

        [TestMethod]
        public void Lane_is_keyed_by_thread_id_parity() {
            Assert.IsFalse(CoopMessageSender.IsWebhookLane(1000ul));
            Assert.IsTrue(CoopMessageSender.IsWebhookLane(1001ul));
        }
    }
}
