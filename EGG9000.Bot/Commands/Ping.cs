using Discord.WebSocket;

using EGG9000.Common.Commands;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class PingCommands {
        [SlashCommand(Description = "Test to see if bot is alive")]
        public static async Task Ping(FauxCommand command) {
            await command.RespondAsync("Pong!", ephemeral: false);
        }

        [SlashCommand(Description = "Test to see if bot is alive/check version", AdminOnly = true, AllowFarmHand = true, ParentCommand = "a")]
        public static async Task Ping(FauxCommand command, [SlashParam(Required = false)] bool showInChannel = false) {
            var result = GitHelpers.ExecuteGitCommand("log -n 1 --format=\"%s%n%H%n%an <%ae>%n%at\"");

            if(result.ExitCode == 0) {
                var output = result.Output.Trim().Split('\n');
                var commitMessage = output[0];
                var commitHash = output[1];
                var author = output[2];
                var commitTimestamp = output[3];

                var response = $"___Running commit:___\n**Hash:**\t[{commitHash}](https://github.com/kendrome/EGG9000/commit/{commitHash})" +
                    $"\n**Author**:\t{author.Split(" ")[0]}\n**Message**:\t{commitMessage}\n**Timestamp:**\t<t:{commitTimestamp}:R>";

                await command.RespondAsync(response, ephemeral: !showInChannel);
            } else {
                await command.RespondAsync("Failed to retrieve Git version information.", ephemeral: !showInChannel);
            }
        }
    }
}
