using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    public class CoopMessageSenderLaneTests {
        [TestMethod]
        public void Lane_alternates_bot_then_webhook() {
            var sender = new CoopMessageSender(null, null, null);
            var results = new List<bool>();
            for(var i = 0; i < 6; i++) results.Add(sender.NextLaneIsWebhook());
            CollectionAssert.AreEqual(new[] { false, true, false, true, false, true }, results);
        }
    }
}
