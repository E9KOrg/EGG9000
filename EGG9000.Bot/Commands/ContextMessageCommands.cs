
using Discord.WebSocket;
using EGG9000.Common.Commands;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class ContextMessageCommands {
        [MessageCommand(Name = "Test Reply", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task TestReply(SocketMessageCommand command) {
            await command.RespondAsync(
                "",
                embed: EmbedSuccess($"Test reply from MessageCommand\ncommand.Data.Message.Id;: {command.Data.Message.Id}")
            );
        }

        /*[MessageCommand(Name = "Reply as E9K", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task ReplyAsE9K(SocketMessageCommand command) {
            command.RespondWithModalAsync()
        }*/
    }
}