using Discord.WebSocket;
using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public record Admin_InactivePlayersModel(
        List<DBUser> users,
        IReadOnlyCollection<SocketGuildUser> guildUsers,
        List<UserCoopXref> xrefs
    );
}
