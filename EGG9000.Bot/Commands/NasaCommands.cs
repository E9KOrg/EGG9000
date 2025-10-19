using Discord.WebSocket;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
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
                await component.RespondAsync("", embed: failureEmbed, ephemeral: false);
                return;
            }
            var explainEmbed = EmbedHelpers.EmbedCustom(EmbedHelpers.EmbedType.Success, "APOD Explanation", explanation);
            await component.RespondAsync("", embed: explainEmbed, ephemeral: false);
        }

    }
}
