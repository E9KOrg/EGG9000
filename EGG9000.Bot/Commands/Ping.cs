using Discord;
using Discord.Interactions;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using System;
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

        private static string GitHubUser(string author, string email) {
            const string noreply = "@users.noreply.github.com";
            if(email.EndsWith(noreply, StringComparison.OrdinalIgnoreCase)) {
                var local = email[..^noreply.Length];
                return local[(local.IndexOf('+') + 1)..];
            }
            return author.Length == 0 || author.Contains(' ') ? null : author;
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
            var authorLine = Line(2);
            var commitTimestamp = Line(3);
            var branch = Line(4);
            var repoUrl = NormalizeRemote(Line(5));

            var emailStart = authorLine.IndexOf('<');
            var author = emailStart > 0 ? authorLine[..emailStart].Trim() : authorLine;
            var email = emailStart > 0 ? authorLine[(emailStart + 1)..].TrimEnd('>').Trim() : string.Empty;

            var githubUser = GitHubUser(author, email);
            var authorText = githubUser is null ? author : $"[{author}](<https://github.com/{githubUser}>)";
            var branchText = string.IsNullOrEmpty(branch) ? string.Empty : $" ([{branch}]({repoUrl}/tree/{branch}))";
            var textFormat = $"Running commit **[{commitHash}](<{repoUrl}/commit/{commitHash}>)**{branchText}  by {authorText} <t:{commitTimestamp}:R>";
            List<EmbedFieldBuilder> fields = [
                new EmbedFieldBuilder().WithName("Message").WithValue(commitMessage),
            ];
            var pongEmbed = EmbedHelpers.EmbedCustom(EmbedHelpers.EmbedType.UnStyled, "Pong!", textFormat, fields);
            await Context.Interaction.RespondAsync(embed: pongEmbed);
        }
    }

}
