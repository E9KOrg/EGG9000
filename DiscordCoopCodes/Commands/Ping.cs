using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DiscordCoopCodes.Commands {
    public class Ping {
        public static async Task ExecuteAsync(SocketMessage message) {
            await message.Channel.SendMessageAsync("Pong!");
        }
    }
}
