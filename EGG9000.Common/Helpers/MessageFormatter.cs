using EGG9000.Common.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class MessageFormatter {
        private static readonly Regex TokenRegex = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

        public static async Task<string> FormatAsync(
            string text,
            IReadOnlyDictionary<string, string> values = null,
            Func<string, Task<string>> resolveCommand = null,
            Func<string, Task<string>> resolveEmoji = null) {
            if(string.IsNullOrEmpty(text)) return text;

            foreach(Match m in TokenRegex.Matches(text)) {
                var body = m.Groups[1].Value;
                var colon = body.IndexOf(':');
                string replacement = null;

                if(colon < 0) {
                    if(values != null && values.TryGetValue(body, out var v)) replacement = v;
                } else {
                    var prefix = body[..colon];
                    var arg = body[(colon + 1)..];
                    if(prefix.Equals("command", StringComparison.OrdinalIgnoreCase) && resolveCommand != null)
                        replacement = await resolveCommand(arg);
                    else if(prefix.Equals("emoji", StringComparison.OrdinalIgnoreCase) && resolveEmoji != null)
                        replacement = await resolveEmoji(arg);
                }

                if(replacement != null) text = text.Replace(m.Value, replacement);
            }

            return text;
        }

        public static Task<string> FormatAsync(
            string text, DiscordHostedService client, ulong guildId,
            IReadOnlyDictionary<string, string> values = null) {
            var guild = client.Guilds.FirstOrDefault(g => g.Id == guildId);
            if(guild is null) return FormatAsync(text, values);

            return FormatAsync(text, values,
                resolveCommand: name => client.GetSlashCommandStringAsync(guild, name),
                resolveEmoji: async name => {
                    var appEmojis = await client.GetApplicationEmotesAsync();
                    var emoji = appEmojis.FirstOrDefault(e => e.Name.ToLower() == name.ToLower())
                        ?? guild.Emotes.FirstOrDefault(e => e.Name.ToLower() == name.ToLower());
                    return emoji is null ? null : $"<:{emoji.Name}:{emoji.Id}>";
                });
        }
    }
}
