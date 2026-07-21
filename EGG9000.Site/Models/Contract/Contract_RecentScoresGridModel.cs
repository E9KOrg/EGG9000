using Discord.WebSocket;

namespace EGG9000.Site.Models.Contract {
    public record Contract_RecentScoresGridModel(Contract_ScoreGridContract[] Contracts, Contract_ScoreGridItem[] GridItems, SocketRole[] Roles);
}
