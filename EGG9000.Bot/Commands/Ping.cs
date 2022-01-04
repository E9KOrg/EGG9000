using Discord.WebSocket;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class PingCommands {
        [SlashCommand(Description = "Test to see if bot is alive")]
        public static async Task Ping(SocketSlashCommand command) {
            await command.RespondAsync("Pong!", ephemeral: false);
        }

        [SlashCommand(Description = "Test to see if bot is alive", AdminOnly = true)]
        public static async Task PingAdmin(SocketSlashCommand command) {
            await command.RespondAsync("Pong Admin!", ephemeral: false);
        }
    }
}
