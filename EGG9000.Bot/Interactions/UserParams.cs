using Discord.WebSocket;
using System.Linq;

namespace EGG9000.Bot.Interactions {
    public static class UserParams {
        public static SocketGuildUser[] CoalesceGuildUsers(params SocketGuildUser[] users) =>
            users.Where(u => u is not null).ToArray();
        public static SocketUser[] CoalesceUsers(params SocketUser[] users) =>
            users.Where(u => u is not null).ToArray();
    }
}
