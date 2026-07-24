using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;

namespace EGG9000.Common.Helpers {
    public static partial class EggHuntScanner {
        [GeneratedRegex(@"(\d+)/(\d+)/(\d+)")]
        private static partial Regex LinkRegex();

        public static async Task<Dictionary<RestGuildUser, int>> CountReactionsAsync(DiscordRestClient rest, string links, string emojiSubstring, Func<IUser, bool> exclude) {
            var eggsFound = new Dictionary<RestGuildUser, int>();
            var byId = new Dictionary<ulong, RestGuildUser>();

            foreach(Match match in LinkRegex().Matches(links)) {
                var guild = await rest.GetGuildAsync(ulong.Parse(match.Groups[1].Value));
                var channel = await guild.GetTextChannelAsync(ulong.Parse(match.Groups[2].Value));
                var message = await channel.GetMessageAsync(ulong.Parse(match.Groups[3].Value));
                var reaction = message.Reactions.FirstOrDefault(x => x.Key.Name.Contains(emojiSubstring));
                if(reaction.Key is null) continue;
                var userReactions = await message.GetReactionUsersAsync(reaction.Key, 9999).FlattenAsync();
                foreach(var user in userReactions) {
                    if(exclude(user)) continue;
                    if(byId.TryGetValue(user.Id, out var existing)) {
                        eggsFound[existing]++;
                        continue;
                    }
                    var guildUser = await guild.GetUserAsync(user.Id);
                    if(guildUser is null) continue;
                    byId[user.Id] = guildUser;
                    eggsFound[guildUser] = 1;
                }
            }

            return eggsFound;
        }
    }
}
