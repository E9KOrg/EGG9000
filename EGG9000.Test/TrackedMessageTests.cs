using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    public class TrackedMessageTests {
        [TestMethod]
        public void Deserialize_old_plain_ulong_array_treats_all_as_bot_lane() {
            var result = TrackedMessageSerializer.Deserialize("[123,456,789]");
            Assert.AreEqual(3, result.Count);
            Assert.IsTrue(result.TrueForAll(x => x.WebhookId == null));
            Assert.AreEqual(123ul, result[0].MessageId);
            Assert.AreEqual(789ul, result[2].MessageId);
        }

        [TestMethod]
        public void Deserialize_new_format_preserves_webhook_id() {
            var result = TrackedMessageSerializer.Deserialize("[{\"MessageId\":123,\"WebhookId\":null},{\"MessageId\":456,\"WebhookId\":999}]");
            Assert.AreEqual(2, result.Count);
            Assert.IsNull(result[0].WebhookId);
            Assert.AreEqual(999ul, result[1].WebhookId);
        }

        [TestMethod]
        public void Deserialize_empty_or_null_returns_empty_list() {
            Assert.AreEqual(0, TrackedMessageSerializer.Deserialize(null).Count);
            Assert.AreEqual(0, TrackedMessageSerializer.Deserialize("").Count);
            Assert.AreEqual(0, TrackedMessageSerializer.Deserialize("[]").Count);
        }

        [TestMethod]
        public void Serialize_then_deserialize_round_trips() {
            var messages = new List<TrackedMessage> { new(111, null), new(222, 333) };
            var json = TrackedMessageSerializer.Serialize(messages);
            var result = TrackedMessageSerializer.Deserialize(json);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(111ul, result[0].MessageId);
            Assert.IsNull(result[0].WebhookId);
            Assert.AreEqual(222ul, result[1].MessageId);
            Assert.AreEqual(333ul, result[1].WebhookId);
        }
    }
}
