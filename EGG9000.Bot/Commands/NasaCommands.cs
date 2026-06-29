using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class NasaModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordHostedService client, ILogger<NasaModule> logger) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {
        private readonly DiscordHostedService _client = client;
        private readonly ILogger<NasaModule> _logger = logger;

        [ComponentInteraction("APODExplanation:*", ignoreGroupNames: true)]
        public async Task APODExplanation(string data) {
            var apodId = System.Guid.Parse(data);
            var explanation = await NasaHelper.GetExplanationOrEmpty(apodId, Db);
            if (string.IsNullOrEmpty(explanation)) {
                var failureEmbed = EmbedHelpers.EmbedWarning("No explanation found for this APOD.");
                await Context.Interaction.RespondAsync("", embed: failureEmbed, ephemeral: true);
                return;
            }
            var explainEmbed = EmbedHelpers.MakeCustomEmbed(EmbedHelpers.EmbedType.Success, "APOD Explanation", explanation);
            await Context.Interaction.RespondAsync("", embed: explainEmbed, ephemeral: true);
        }

        [SlashCommand("apod", "View NASA's latest Astronomy Picture of the Day (APOD)")]
        public async Task APOD() {
            var latestApod = await NasaHelper.GetLatestApod(Db);
            if (latestApod is null) {
                var failureEmbed = EmbedHelpers.EmbedWarning("No APOD found in the database.");
                await Context.Interaction.RespondAsync("", embed: failureEmbed, ephemeral: true);
                return;
            }
            await Context.Interaction.TrySendLatestNasaAPODAdHoc(latestApod, _client, Db, _logger);
        }
    }
}
