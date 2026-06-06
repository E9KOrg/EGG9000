using System.Collections.Generic;

namespace EGG9000.Common.Helpers {
    public class DiscordMessageSplitter {
        public static List<string> SplitMessage(string msg, string splitAt) {
            var results = new List<string>();
            while(msg.Length > 2000) {
                var index = msg.LastIndexOf(splitAt, 2000);

                results.Add(msg.Substring(0, index));
                msg = msg.Substring(index);
            }
            results.Add(msg);

            return results;
        }
    }
}
