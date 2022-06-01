using Discord.Rest;
using Discord.WebSocket;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class DiscordMessageSplitter {
        public static async Task<List<RestUserMessage>> SendMessageSplitAsync(ISocketMessageChannel channel, string text, string splitAt) {
            var results = new List<RestUserMessage>();
            while(text.Length > 2000) {
                var index = text.LastIndexOf(splitAt, 2000);

                results.Add(await channel.SendMessageAsync(text.Substring(0, index)));
                text = text.Substring(index);
            }
            results.Add(await channel.SendMessageAsync(text));

            return results;
        }

    }
}
