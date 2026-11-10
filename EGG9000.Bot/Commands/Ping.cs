using Discord;
using Discord.Interactions;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class PingModule(IDbContextFactory<ApplicationDbContext> dbFactory) : E9KModuleBase(dbFactory) {

        private const string DefaultRepoUrl = "https://github.com/E9KOrg/EGG9000";

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
            string gitVersion;
            using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("EGG9000.Bot.version.txt"))
            using(var reader = new StreamReader(stream)) {
                gitVersion = reader.ReadToEnd();
            }

            var info = gitVersion.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => (Separator: line.IndexOf('='), Line: line))
                .Where(x => x.Separator > 0)
                .ToDictionary(x => x.Line[..x.Separator], x => x.Line[(x.Separator + 1)..].Trim());
            string Value(string key) => info.GetValueOrDefault(key, string.Empty);

            var commitMessage = Value("subject");
            var commitHash = Value("hash");
            var author = Value("author");
            var email = Value("email");
            var commitTimestamp = Value("timestamp");
            var branch = Value("branch");
            var repoUrl = NormalizeRemote(Value("remote"));

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
