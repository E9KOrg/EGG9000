using Discord.WebSocket;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Services;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public static class NasaCommands {
        [ComponentCommand]
        public static async Task APODExplanation(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var apodId = System.Guid.Parse(data);
            var explanation = await NasaHelper.GetExplanationOrEmpty(apodId, db);
            if (explanation.IsNullOrEmpty()) {
                var failureEmbed = EmbedHelpers.EmbedWarning("No explanation found for this APOD.");
                await component.RespondAsync("", embed: failureEmbed, ephemeral: true);
                return;
            }
            var explainEmbed = EmbedHelpers.MakeCustomEmbed(EmbedHelpers.EmbedType.Success, "APOD Explanation", explanation);
            await component.RespondAsync("", embed: explainEmbed, ephemeral: true);
        }

        [SlashCommand(Description = "View NASA's latest Astronomy Picture of the Day (APOD)")]
        public static async Task APOD(FauxCommand command, DiscordHostedService client, ApplicationDbContext db, ILogger logger) {
            var latestApod = await NasaHelper.GetLatestApod(db);
            if (latestApod is null) {
                var failureEmbed = EmbedHelpers.EmbedWarning("No APOD found in the database.");
                await command.RespondAsync("", embed: failureEmbed, ephemeral: true);
                return;
            }
            await command.TrySendLatestNasaAPODAdHoc(latestApod, client, db, logger);
        }
    }
}
