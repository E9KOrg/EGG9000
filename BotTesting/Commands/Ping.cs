using EGG9000.Common.Commands;
using EGG9000.Common.Services;

namespace TestBot.Commands {
    public class PingCommands {
        [SlashCommand(Description = "Test to see if bot is alive")]
        public static async Task Ping(FauxCommand command) {
            await command.RespondAsync("Pong!", ephemeral: false);
        }

        [SlashCommand(Description = "Test to see if bot is alive", AdminOnly = true)]
        public static async Task PingAdmin(FauxCommand command) {
            await command.RespondAsync("Pong Admin!", ephemeral: false);
        }
    }
}
