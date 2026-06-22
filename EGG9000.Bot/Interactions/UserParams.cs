using Discord.WebSocket;

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EGG9000.Bot.Interactions {
    public static class UserParams {
        public static SocketGuildUser[] CoalesceGuildUsers(params SocketGuildUser[] users) =>
            users.Where(u => u is not null).ToArray();
        public static SocketUser[] CoalesceUsers(params SocketUser[] users) =>
            users.Where(u => u is not null).ToArray();

        private static readonly Regex MentionRx = new(@"<@!?(?<id>\d{15,21})>|\b(?<id2>\d{15,21})\b", RegexOptions.Compiled);

        // Parses "<@123> <@456> 789" (mentions or raw IDs, separated by anything) into resolved
        // SocketUser[]. Unknown IDs (not in cache) become null entries in `missing`. Use this to
        // restore the pre-migration "free-form list of users" UX in a single text option, since
        // the Discord slash schema has no array option type.
        public static SocketUser[] ParseUsers(string input, DiscordSocketClient gateway, out List<ulong> missing) {
            missing = new List<ulong>();
            if(string.IsNullOrWhiteSpace(input)) return [];
            var seen = new HashSet<ulong>();
            var result = new List<SocketUser>();
            foreach(Match m in MentionRx.Matches(input)) {
                var raw = m.Groups["id"].Success ? m.Groups["id"].Value : m.Groups["id2"].Value;
                if(!ulong.TryParse(raw, out var id) || !seen.Add(id)) continue;
                var u = (SocketUser)gateway.GetUser(id);
                if(u is null) missing.Add(id); else result.Add(u);
            }
            return result.ToArray();
        }

        public static SocketGuildUser[] ParseGuildUsers(string input, SocketGuild guild, out List<ulong> missing) {
            missing = new List<ulong>();
            if(string.IsNullOrWhiteSpace(input) || guild is null) return [];
            var seen = new HashSet<ulong>();
            var result = new List<SocketGuildUser>();
            foreach(Match m in MentionRx.Matches(input)) {
                var raw = m.Groups["id"].Success ? m.Groups["id"].Value : m.Groups["id2"].Value;
                if(!ulong.TryParse(raw, out var id) || !seen.Add(id)) continue;
                var u = guild.GetUser(id);
                if(u is null) missing.Add(id); else result.Add(u);
            }
            return result.ToArray();
        }
    }
}
