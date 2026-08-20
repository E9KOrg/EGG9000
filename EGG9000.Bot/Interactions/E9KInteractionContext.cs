using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Database.Entities;

namespace EGG9000.Bot.Interactions {
    public class E9KInteractionContext(DiscordSocketClient client, SocketInteraction interaction) : SocketInteractionContext(client, interaction) {
        public Coop CoopChannel { get; set; }
        public GuildContract ContractChannel { get; set; }
    }
}
