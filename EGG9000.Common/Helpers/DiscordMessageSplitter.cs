using Discord;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class DiscordMessageSplitter {
        public static async Task<List<IUserMessage>> SendMessageSplitAsync(IMessageChannel channel, string text, string splitAt) {
            var msgs = SplitMessage(text, splitAt);
            var results = new List<IUserMessage>();
            foreach(var msg in msgs) {
                results.Add(await channel.SendMessageAsync(msg));
            }

            return results;
        }

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
