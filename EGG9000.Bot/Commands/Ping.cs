using EGG9000.Common.Commands;
using EGG9000.Common.Services;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Commands {
    public class PingCommands {
        [SlashCommand(Description = "Test to see if bot is alive", AllowInDMs = true)]
        public static async Task Ping(FauxCommand command) {
            await command.RespondAsync("Pong!", ephemeral: false);
        }

        [SlashCommand(Description = "Test to see if bot is alive/check version", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task Ping(FauxCommand command, ILogger _logger, [SlashParam(Required = false)] bool showInChannel = false) {

            var gitVersion = string.Empty;
    
            using(var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("EGG9000.Bot.version.txt"))
            using(var reader = new StreamReader(stream)) {
                gitVersion = reader.ReadToEnd();
            }

            var output = gitVersion.Trim().Split('\n');
            var commitMessage = output[0];
            var commitHash = output[1];
            var author = output[2];
            var commitTimestamp = output[3];

            var response = $"___Running commit:___\n**Hash:**\t[{commitHash}](https://github.com/kendrome/EGG9000/commit/{commitHash})" +
                $"\n**Author**:\t{author.Split(" ")[0]}\n**Message**:\t{commitMessage}\n**Timestamp:**\t<t:{commitTimestamp}:R>";

            _logger.LogInformation($"Responding to ping...");
            await command.RespondAsync(response, ephemeral: !showInChannel);
            _logger.LogInformation($"Responded to ping, {command.HasResponded}");
        }
    }
}
