using Discord.Interactions;
using Discord.WebSocket;

using System.Linq;

namespace EGG9000.Bot.Interactions {
    // Discord's slash command schema has no array option type, so a real "tag several users"
    // param has to be several individual User-type options under the hood (each gets Discord's
    // own full-guild user search, unlike a free-text field which only autocompletes users visible
    // in the current channel). [ComplexParameter] lets every command declare this once as a single
    // `[ComplexParameter] UserSlots users` parameter instead of repeating 8 ctor params each time.
    public class UserSlots {
        public SocketGuildUser[] Users { get; }

        [ComplexParameterCtor]
        public UserSlots(
            [Summary("user1")] SocketGuildUser user1,
            [Summary("user2")] SocketGuildUser user2 = null,
            [Summary("user3")] SocketGuildUser user3 = null,
            [Summary("user4")] SocketGuildUser user4 = null,
            [Summary("user5")] SocketGuildUser user5 = null,
            [Summary("user6")] SocketGuildUser user6 = null,
            [Summary("user7")] SocketGuildUser user7 = null,
            [Summary("user8")] SocketGuildUser user8 = null) {
            Users = new[] { user1, user2, user3, user4, user5, user6, user7, user8 }.Where(u => u is not null).ToArray();
        }
    }
}
