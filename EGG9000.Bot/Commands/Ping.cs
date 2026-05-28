using Discord.Interactions;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Commands {
    public class PingModule : E9KModuleBase {
        public PingModule(IDbContextFactory<ApplicationDbContext> dbFactory) : base(dbFactory) { }

        [SlashCommand("ping", "Test to see if bot is alive")]
        [EnabledInDm(true)]
        public async Task Ping() {
            await Context.Interaction.RespondAsync("Pong!", ephemeral: false);
        }
    }

    public partial class AdminModule {
        [SlashCommand("ping", "Test to see if bot is alive/check version")]
        public async Task Ping([Summary("showinchannel")] bool showInChannel = false) {

            var gitVersion = string.Empty;

            using(var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("EGG9000.Bot.version.txt"))
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

            var branchLine = string.IsNullOrEmpty(branch) ? string.Empty : $"\n**Branch:**\t{branch}";
            var response = $"___Running commit:___\n**Hash:**\t[{commitHash}]({repoUrl}/commit/{commitHash}){branchLine}" +
                $"\n**Author**:\t{author}\n**Message**:\t{commitMessage}\n**Timestamp:**\t<t:{commitTimestamp}:R>";

            _logger.LogInformation($"Responding to ping...");
            await Context.Interaction.RespondAsync(response, ephemeral: !showInChannel);
            _logger.LogInformation($"Responded to ping, {Context.Interaction.HasResponded}");
        }

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
    }
}
