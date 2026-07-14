using Discord;
using Discord.Interactions;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class PingModule(IDbContextFactory<ApplicationDbContext> dbFactory) : E9KModuleBase(dbFactory) {

        private const string DefaultRepoUrl = "https://github.com/E9KOrg/EGG9000";

        // version.txt line 6 is the raw remote.origin.url. Convert ssh/https git URLs to a web URL so the
        // commit hash links to wherever this build was actually pushed (fork or canonical), not a hardcoded
        // org. Falls back to the canonical repo when no remote was captured at build time.
        private static string NormalizeRemote(string raw) {
            raw = raw?.Trim() ?? string.Empty;
            if(raw.Length == 0) return DefaultRepoUrl;
            if(raw.StartsWith("git@")) {
                var colon = raw.IndexOf(':');
                if(colon > 4) raw = $"https://{raw[4..colon]}/{raw[(colon + 1)..]}";
            }
            if(raw.EndsWith(".git")) raw = raw[..^4];
            return raw;
        }

        [SlashCommand("ping", "Test to see if bot is alive")]
        [CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
        public async Task Ping() {
            var gitVersion = string.Empty;

            using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EGG9000.Bot.version.txt"))
            using(var reader = new StreamReader(stream)) {
                gitVersion = reader.ReadToEnd();
            }

            var output = gitVersion.Replace("\r", string.Empty).Trim().Split('\n');
            string Line(int i) => i < output.Length ? output[i].Trim() : string.Empty;

            var commitMessage = Line(0);
            var commitHash = Line(1);
            var author = Line(2);
            var commitTimestamp = Line(3);
            var branch = Line(4);
            var repoUrl = NormalizeRemote(Line(5));

            var emailStart = author.IndexOf('<');
            if(emailStart > 0) author = author[..emailStart].Trim();

            var authorLink = $"https://github.com/{author}";
            var branchText = string.IsNullOrEmpty(branch) ? string.Empty : $" ([{branch}]({repoUrl}/tree/{branch}))";
            var textFormat = $"Running commit **[{commitHash}](<{repoUrl}/commit/{commitHash}>)**{branchText}  by [{author}](<{authorLink}>) <t:{commitTimestamp}:R>";
            List<EmbedFieldBuilder> fields = [
                new EmbedFieldBuilder().WithName("Message").WithValue(commitMessage),
            ];
            var pongEmbed = EmbedHelpers.EmbedCustom(EmbedHelpers.EmbedType.UnStyled, "Pong!", textFormat, fields);
            await Context.Interaction.RespondAsync(embed: pongEmbed);
        }
    }

}
