using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public record TrackedMessage(ulong MessageId, ulong? WebhookId);

    public static class TrackedMessageSerializer {
        public static List<TrackedMessage> Deserialize(string json) {
            if(string.IsNullOrWhiteSpace(json)) return [];

            var token = JToken.Parse(json);
            if(token.Type != JTokenType.Array) return [];

            var array = (JArray)token;
            if(array.Count == 0) return [];

            // Old format: a plain array of message-id numbers, e.g. [123,456].
            // New format: an array of {"MessageId":..,"WebhookId":..} objects.
            if(array[0].Type == JTokenType.Integer) {
                return [.. array.ToObject<List<ulong>>().Select(id => new TrackedMessage(id, null))];
            }

            return array.ToObject<List<TrackedMessage>>() ?? [];
        }

        public static string Serialize(List<TrackedMessage> messages) => JsonConvert.SerializeObject(messages);
    }
}
