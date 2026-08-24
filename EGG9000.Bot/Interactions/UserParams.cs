using Discord.Interactions;
using Discord.WebSocket;

using System.Linq;

namespace EGG9000.Bot.Interactions {
    // Discord's slash command schema has no array option type, so a real "tag several users"
    // param has to be several individual User-type options under the hood (each gets Discord's
    // own full-guild user search, unlike a free-text field which only autocompletes users visible
    // in the current channel). [ComplexParameter] lets every command declare this once as a single
    // `[ComplexParameter] UserSlots users` parameter instead of repeating 8 ctor params each time.
    //
    // SocketGuildUser resolution requires the target to currently be a cached guild member, so it
    // fails (with a misleading "cannot be read as IChannel" error from Discord.Net) for anyone who
    // has left the server. Use UserSlots (SocketUser) for anything that should still work on a
    // departed user (ban, kick, merit); use GuildUserSlots only when the command needs guild-member
    // state, e.g. role assignment.
    [method: ComplexParameterCtor]
    public class UserSlots(
        [Summary("user1")] SocketUser user1,
        [Summary("user2")] SocketUser user2 = null,
        [Summary("user3")] SocketUser user3 = null,
        [Summary("user4")] SocketUser user4 = null,
        [Summary("user5")] SocketUser user5 = null,
        [Summary("user6")] SocketUser user6 = null,
        [Summary("user7")] SocketUser user7 = null,
        [Summary("user8")] SocketUser user8 = null) {
        public SocketUser[] Users { get; } = new[] { user1, user2, user3, user4, user5, user6, user7, user8 }.Where(u => u is not null).ToArray();
    }

    [method: ComplexParameterCtor]
    public class GuildUserSlots(
        [Summary("user1")] SocketGuildUser user1,
        [Summary("user2")] SocketGuildUser user2 = null,
        [Summary("user3")] SocketGuildUser user3 = null,
        [Summary("user4")] SocketGuildUser user4 = null,
        [Summary("user5")] SocketGuildUser user5 = null,
        [Summary("user6")] SocketGuildUser user6 = null,
        [Summary("user7")] SocketGuildUser user7 = null,
        [Summary("user8")] SocketGuildUser user8 = null) {
        public SocketGuildUser[] Users { get; } = new[] { user1, user2, user3, user4, user5, user6, user7, user8 }.Where(u => u is not null).ToArray();
    }
}
